using Brutal.JoltApi;
using Brutal.Numerics;
using FullThrust.Core.Physics;
using FullThrust.Core.Space;
using JoltNative = Brutal.JoltApi.Jolt;

namespace FullThrust.Physics.Jolt;

// Jolt is single precision, so callers keep bodies near the origin of a PhysicsFrame and hold world state in doubles.
public sealed class JoltRigidBodyWorld : IRigidBodyWorld {

    private const ushort GroundLayer = 0;
    private const ushort CraftLayer = 1;

    private const uint InvalidBody = 0xFFFFFFFF;

    // Jolt rejects very small height fields.
    private const int GroundGridPrime = 16;

    private struct Slot {

        public bool Alive;

        public uint Id;

        // Kept so a moving centre of mass can be re-wrapped without rebuilding the compound.
        public Shape Inner;
        public double3 CenterOfMass;

    }

    private static readonly Lock GlobalLock = new();

    private static int _instances;

    private readonly TempAllocatorImpl _temp;
    private readonly JobSystemThreadPool _jobs;

    private readonly BroadPhaseLayerInterfaceTable _broadPhaseLayers;
    private readonly ObjectLayerPairFilterTable _layerPairs;
    private readonly ObjectVsBroadPhaseLayerFilterTable _broadPhaseFilter;

    private readonly PhysicsSystem _system;
    private readonly BodyInterface _bodies;

    private readonly MassProperties _massProperties;

    private readonly ShapeSettings[] _scratch = new ShapeSettings[256];

    private ShapeSettings[] _triangles = new ShapeSettings[2 * 63 * 63];

    private readonly Slot[] _slots;
    private readonly int[] _free;

    private int _freeCount;
    private int _next;

    private bool _disposed;

    public JoltRigidBodyWorld(int maxBodies = 256, int workerThreads = 2) {

        AcquireGlobals();

        _temp = TempAllocatorImpl.Create(16 * 1024 * 1024);
        _jobs = JobSystemThreadPool.Create(2048, 8, workerThreads);

        _broadPhaseLayers = BroadPhaseLayerInterfaceTable.Create(2, 2);
        _broadPhaseLayers.MapObjectToBroadPhaseLayer(GroundLayer, 0);
        _broadPhaseLayers.MapObjectToBroadPhaseLayer(CraftLayer, 1);

        // Separate craft never collide: staging is driven by decoupler impulses, not contact.
        _layerPairs = ObjectLayerPairFilterTable.Create(2);
        _layerPairs.EnableCollision(GroundLayer, CraftLayer);

        _broadPhaseFilter = ObjectVsBroadPhaseLayerFilterTable.Create(_broadPhaseLayers, 2, _layerPairs, 2);

        _system = PhysicsSystem.Create();
        _system.Init((uint)maxBodies, 0, 4096, 4096, _broadPhaseLayers, _broadPhaseFilter, _layerPairs);
        _system.SetGravity(float3.Zero);

        _bodies = _system.GetBodyInterface();
        _massProperties = MassProperties.Create();

        _slots = new Slot[maxBodies];
        _free = new int[maxBodies];

        Prime();

    }

    // Each binding allocates its interop stub on first call, so touch every path now rather than mid-flight.
    private void Prime() {

        Span<ShapeElement> shape = [ShapeElement.Cylinder(double3.Zero, 0.5, 0.5), ShapeElement.Cylinder(new double3(0, 1, 0), 0.5, 0.5)];

        var mass = new MassDistribution { Mass = 1, Inertia = InertiaTensor.SolidCylinder(1, 0.5, 2) };
        var dynamic = CreateDynamic(shape, mass, new RigidBodyState { Rotation = doubleQuat.Identity });
        var kinematic = CreateKinematic([new ShapeElement(ShapePrimitive.Box, double3.Zero, doubleQuat.Identity, new double3(1, 1, 1))], new double3(0, -10, 0), doubleQuat.Identity);

        mass.CenterOfMass = new double3(0, 1, 0);

        SetShape(dynamic, shape[..1], double3.Zero);
        SetMass(dynamic, mass);
        SetState(dynamic, GetState(dynamic));
        SetState(kinematic, GetState(kinematic));
        MoveKinematic(kinematic, new double3(0, -10, 0), doubleQuat.Identity, 0.02);
        AddForceAndTorque(dynamic, double3.UnitY, double3.UnitX);

        Span<float> heights = stackalloc float[GroundGridPrime * GroundGridPrime];

        var field = CreateKinematicHeightField(new HeightGrid(heights, GroundGridPrime, 1, double3.Zero), new double3(0, -20, 0), doubleQuat.Identity);

        SetHeightField(field, new HeightGrid(heights, GroundGridPrime, 1, double3.Zero));
        MoveKinematic(field, new double3(0, -20, 0), doubleQuat.Identity, 0.02);
        ShiftFrame(double3.UnitX, double3.UnitX);
        Step(0.02);

        Destroy(field);
        Destroy(kinematic);
        Destroy(dynamic);

        _next = 0;
        _freeCount = 0;

    }

    public double CenterOfMassTolerance { get; set; } = 0.02;

    public int BodyCount { get; private set; }

    public PhysicsUpdateError LastError { get; private set; }

    public int CreateDynamic(ReadOnlySpan<ShapeElement> shape, in MassDistribution mass, in RigidBodyState state) {

        var inner = BuildShape(shape);
        var wrapped = OffsetShape(inner, mass.CenterOfMass);

        using var settings = BodyCreationSettings.Create(wrapped, SpaceMath.ToFloat(state.Position), SpaceMath.ToFloat(state.Rotation), MotionType.Dynamic, CraftLayer);

        settings.SetAllowSleeping(false);
        settings.SetLinearDamping(0);
        settings.SetAngularDamping(0);
        settings.SetMaxLinearVelocity(10_000);
        settings.SetApplyGyroscopicForce(true);
        settings.SetFriction(0.8f);
        settings.SetRestitution(0.05f);

        // Tall stacks standing on one end rock in the contact solver at the default 10 iterations.
        settings.SetNumVelocityStepsOverride(40);
        settings.SetNumPositionStepsOverride(4);

        var id = _bodies.CreateAndAddBody(settings, Activation.Activate);

        wrapped.Dispose();

        var handle = Register(id, inner, mass.CenterOfMass);

        ApplyMass(id, in mass);
        _bodies.SetLinearAndAngularVelocity(id, SpaceMath.ToFloat(state.LinearVelocity), SpaceMath.ToFloat(state.AngularVelocity));

        return handle;

    }

    public int CreateKinematic(ReadOnlySpan<ShapeElement> shape, double3 position, doubleQuat rotation) {

        var inner = BuildShape(shape);

        using var settings = BodyCreationSettings.Create(inner, SpaceMath.ToFloat(position), SpaceMath.ToFloat(rotation), MotionType.Kinematic, GroundLayer);

        settings.SetAllowSleeping(false);
        settings.SetFriction(0.8f);

        var id = _bodies.CreateAndAddBody(settings, Activation.Activate);

        return Register(id, inner, double3.Zero);

    }

    public int CreateKinematicHeightField(in HeightGrid shape, double3 position, doubleQuat rotation) {

        var inner = BuildHeightField(in shape);

        using var settings = BodyCreationSettings.Create(inner, SpaceMath.ToFloat(position), SpaceMath.ToFloat(rotation), MotionType.Kinematic, GroundLayer);

        settings.SetAllowSleeping(false);
        settings.SetFriction(0.8f);

        var id = _bodies.CreateAndAddBody(settings, Activation.Activate);

        return Register(id, inner, double3.Zero);

    }

    public void SetHeightField(int body, in HeightGrid shape) {

        ref var slot = ref Get(body);

        var inner = BuildHeightField(in shape);

        _bodies.SetShape(slot.Id, inner, false, Activation.DontActivate);

        slot.Inner.Dispose();
        slot.Inner = inner;

    }

    public void Destroy(int body) {

        ref var slot = ref Get(body);

        _bodies.RemoveBody(slot.Id);
        _bodies.DestroyBody(slot.Id);
        slot.Inner.Dispose();

        slot = default;

        _free[_freeCount++] = body;
        BodyCount--;

    }

    public void SetShape(int body, ReadOnlySpan<ShapeElement> shape, double3 centerOfMass) {

        ref var slot = ref Get(body);

        var inner = BuildShape(shape);

        slot.Inner.Dispose();
        slot.Inner = inner;

        Rewrap(ref slot, centerOfMass);

    }

    public void SetMass(int body, in MassDistribution mass) {

        ref var slot = ref Get(body);

        if (double3.DistanceSquared(slot.CenterOfMass, mass.CenterOfMass) > CenterOfMassTolerance * CenterOfMassTolerance) {

            Rewrap(ref slot, mass.CenterOfMass);

        }

        ApplyMass(slot.Id, in mass);

    }

    public RigidBodyState GetState(int body) {

        var id = Get(body).Id;

        _bodies.GetPositionAndRotation(id, out var position, out var rotation);
        _bodies.GetLinearAndAngularVelocity(id, out var linear, out var angular);

        return new RigidBodyState {

            Position = SpaceMath.ToDouble(position),
            Rotation = SpaceMath.ToDouble(rotation),

            LinearVelocity = SpaceMath.ToDouble(linear),
            AngularVelocity = SpaceMath.ToDouble(angular),

            CenterOfMass = SpaceMath.ToDouble(_bodies.GetCenterOfMassPosition(id)),

        };

    }

    public void SetState(int body, in RigidBodyState state) {

        var id = Get(body).Id;

        _bodies.SetPositionAndRotation(id, SpaceMath.ToFloat(state.Position), SpaceMath.ToFloat(state.Rotation), Activation.Activate);
        _bodies.SetLinearAndAngularVelocity(id, SpaceMath.ToFloat(state.LinearVelocity), SpaceMath.ToFloat(state.AngularVelocity));

    }

    public void MoveKinematic(int body, double3 position, doubleQuat rotation, double deltaTime) {

        _bodies.MoveKinematic(Get(body).Id, SpaceMath.ToFloat(position), SpaceMath.ToFloat(rotation), (float)deltaTime);

    }

    public void AddForceAndTorque(int body, double3 force, double3 torque) {

        _bodies.AddForceAndTorque(Get(body).Id, SpaceMath.ToFloat(force), SpaceMath.ToFloat(torque), Activation.Activate);

    }

    public void ShiftFrame(double3 positionDelta, double3 velocityDelta) {

        var dp = SpaceMath.ToFloat(positionDelta);
        var dv = SpaceMath.ToFloat(velocityDelta);

        for (var i = 0; i < _next; i++) {

            ref var slot = ref _slots[i];

            if (!slot.Alive) {

                continue;

            }

            // GetPosition allocates in the current bindings; GetPositionAndRotation does not.
            _bodies.GetPositionAndRotation(slot.Id, out var position, out _);
            _bodies.SetPosition(slot.Id, position - dp, Activation.DontActivate);

            _bodies.SetLinearVelocity(slot.Id, _bodies.GetLinearVelocity(slot.Id) - dv);

        }

    }

    public void Step(double deltaTime) {

        LastError = _system.Update((float)deltaTime, 1, _temp, _jobs);

    }

    private int Register(uint id, Shape inner, double3 centerOfMass) {

        if (id == InvalidBody) {

            inner.Dispose();

            throw new InvalidOperationException("Jolt could not create a body; the body limit may be exhausted.");

        }

        var handle = _freeCount > 0 ? _free[--_freeCount] : _next++;

        if (handle >= _slots.Length) {

            throw new InvalidOperationException("Rigid body table is full.");

        }

        _slots[handle] = new Slot { Alive = true, Id = id, Inner = inner, CenterOfMass = centerOfMass };
        BodyCount++;

        return handle;

    }

    private ref Slot Get(int body) {

        ref var slot = ref _slots[body];

        if (!slot.Alive) {

            throw new InvalidOperationException($"Rigid body {body} does not exist.");

        }

        return ref slot;

    }

    private void Rewrap(ref Slot slot, double3 centerOfMass) {

        var wrapped = OffsetShape(slot.Inner, centerOfMass);

        _bodies.SetShape(slot.Id, wrapped, false, Activation.DontActivate);
        wrapped.Dispose();

        slot.CenterOfMass = centerOfMass;

    }

    private void ApplyMass(uint id, in MassDistribution mass) {

        var i = mass.Inertia;

        float4x4 inertia;

        inertia.X = new float4((float)i.Xx, (float)i.Xy, (float)i.Xz, 0);
        inertia.Y = new float4((float)i.Xy, (float)i.Yy, (float)i.Yz, 0);
        inertia.Z = new float4((float)i.Xz, (float)i.Yz, (float)i.Zz, 0);
        inertia.W = new float4(0, 0, 0, 1);

        _massProperties.SetMass((float)mass.Mass);
        _massProperties.SetInertia(in inertia);

        _system.GetBodyLockInterfaceNoLock().TryGetBody(id).GetMotionProperties().SetMassProperties(AllowedDOFs.All, _massProperties);

    }

    // Jolt places the centre of mass where the shape says, so the craft's own value is imposed with an offset.
    private static Shape OffsetShape(Shape inner, double3 centerOfMass) {

        using var settings = OffsetCenterOfMassShapeSettings.Create(SpaceMath.ToFloat(centerOfMass) - inner.GetCenterOfMass(), inner);

        return Checked(settings.CreateShape());

    }

    private Shape BuildShape(ReadOnlySpan<ShapeElement> shape) {

        if (shape.Length == 0 || shape.Length > _scratch.Length) {

            throw new ArgumentException($"A body needs between 1 and {_scratch.Length} shape elements.", nameof(shape));

        }

        for (var i = 0; i < shape.Length; i++) {

            _scratch[i] = CreatePrimitive(in shape[i]);

        }

        Shape result;

        if (shape.Length == 1) {

            using var single = RotatedTranslatedShapeSettings.Create(SpaceMath.ToFloat(shape[0].Position), SpaceMath.ToFloat(shape[0].Rotation), _scratch[0]);

            result = Checked(single.CreateShape());

        } else {

            using var compound = StaticCompoundShapeSettings.Create();

            for (var i = 0; i < shape.Length; i++) {

                compound.AddShape(SpaceMath.ToFloat(shape[i].Position), SpaceMath.ToFloat(shape[i].Rotation), _scratch[i], 0);

            }

            result = Checked(compound.CreateShape());

        }

        for (var i = 0; i < shape.Length; i++) {

            _scratch[i].Dispose();
            _scratch[i] = default;

        }

        return result;

    }

    // Brutal's height-field and mesh bindings drop span arguments, so the grid becomes a compound of triangle shapes.
    private Shape BuildHeightField(in HeightGrid grid) {

        var n = grid.SampleCount;

        if (n < 2 || grid.Heights.Length < n * n) {

            throw new ArgumentException("A height grid needs SampleCount squared heights.", nameof(grid));

        }

        var count = (n - 1) * (n - 1) * 2;

        if (_triangles.Length < count) {

            _triangles = new ShapeSettings[count];

        }

        var origin = SpaceMath.ToFloat(grid.Origin);
        var spacing = (float)grid.Spacing;
        var t = 0;

        for (var j = 0; j + 1 < n; j++) {

            for (var i = 0; i + 1 < n; i++) {

                var a = origin + new float3(i * spacing, grid.Heights[j * n + i], j * spacing);
                var b = origin + new float3((i + 1) * spacing, grid.Heights[j * n + i + 1], j * spacing);
                var c = origin + new float3(i * spacing, grid.Heights[(j + 1) * n + i], (j + 1) * spacing);
                var d = origin + new float3((i + 1) * spacing, grid.Heights[(j + 1) * n + i + 1], (j + 1) * spacing);

                // Counter-clockwise seen from above (+Y), so the faces point up.
                _triangles[t++] = TriangleShapeSettings.Create(a, c, b, 0, default);
                _triangles[t++] = TriangleShapeSettings.Create(b, c, d, 0, default);

            }

        }

        using var compound = StaticCompoundShapeSettings.Create();

        for (var i = 0; i < count; i++) {

            compound.AddShape(float3.Zero, floatQuat.Identity, _triangles[i], 0);

        }

        var result = Checked(compound.CreateShape());

        for (var i = 0; i < count; i++) {

            _triangles[i].Dispose();
            _triangles[i] = default;

        }

        return result;

    }

    private static ShapeSettings CreatePrimitive(in ShapeElement element) {

        var size = SpaceMath.ToFloat(element.Size);

        if (element.Primitive == ShapePrimitive.Box) {

            var box = BoxShapeSettings.Create();

            box.SetHalfExtent(size);
            box.SetConvexRadius(MathF.Min(0.05f, 0.5f * MathF.Min(size.X, MathF.Min(size.Y, size.Z))));

            return box;

        }

        var cylinder = CylinderShapeSettings.Create();

        cylinder.SetRadius(size.X);
        cylinder.SetHalfHeight(size.Y);
        cylinder.SetConvexRadius(MathF.Min(0.05f, 0.5f * MathF.Min(size.X, size.Y)));

        return cylinder;

    }

    private static Shape Checked(Shape shape) {

        if (shape.IsNull()) {

            throw new InvalidOperationException($"Jolt shape creation failed: {JoltNative.GetLastError()}");

        }

        return shape;

    }

    private static void AcquireGlobals() {

        lock (GlobalLock) {

            if (_instances++ > 0) {

                return;

            }

            JoltNative.RegisterDefaultAllocator();
            Factory.Instance = Factory.Create();
            JoltNative.RegisterTypes();

        }

    }

    private static void ReleaseGlobals() {

        lock (GlobalLock) {

            if (--_instances > 0) {

                return;

            }

            JoltNative.UnregisterTypes();
            Factory.DestroyInstance();

        }

    }

    public void Dispose() {

        if (_disposed) {

            return;

        }

        _disposed = true;

        for (var i = 0; i < _next; i++) {

            if (_slots[i].Alive) {

                Destroy(i);

            }

        }

        _massProperties.Dispose();
        _system.Dispose();

        _broadPhaseFilter.Dispose();
        _layerPairs.Dispose();
        _broadPhaseLayers.Dispose();

        _jobs.Dispose();
        _temp.Dispose();

        ReleaseGlobals();

    }

}

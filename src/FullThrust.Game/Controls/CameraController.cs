using Brutal.GlfwApi;
using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;
using FullThrust.Core.Space;
using FullThrust.Game.Platform;
using FullThrust.Game.Scene;

namespace FullThrust.Game.Controls;

public struct CameraTarget {

    public string Label;

    public Entity Entity;

    public double DefaultDistance;
    public double MinDistance;

}

public sealed class CameraController {

    private readonly InputState _input;
    private readonly DemoScene _scene;

    private readonly List<CameraTarget> _targets = [];

    private int _current;
    private Entity _inspection;

    public CameraController(InputState input, Camera camera, DemoScene scene) {

        _input = input;
        _scene = scene;

        Camera = camera;

    }

    public Camera Camera { get; }

    public OrbitCameraRig Rig { get; } = new() { MaxDistance = 2.0e9 };

    public float MouseSensitivity { get; set; } = 0.0035f;

    public float ZoomStep { get; set; } = 0.12f;

    public int TargetCount => _targets.Count;

    public int TargetIndex => _current;

    public Entity Target => _targets.Count > 0 ? _targets[_current].Entity : Entity.Null;

    public double3 TargetPosition { get; private set; }

    public void AddTarget(CameraTarget target) => _targets.Add(target);

    public void Retarget(int index, Entity entity, double defaultDistance, double minDistance) {

        var target = _targets[index];

        target.Entity = entity;
        target.DefaultDistance = defaultDistance;
        target.MinDistance = minDistance;

        _targets[index] = target;

        if (index == _current) {

            Focus(index);

        }

    }

    public string TargetLabel(int index) => _targets[index].Label;

    public void Focus(int index) {

        if (_targets.Count == 0) {

            return;

        }

        _current = (index % _targets.Count + _targets.Count) % _targets.Count;
        _inspection = Entity.Null;

        var target = _targets[_current];

        Rig.MinDistance = target.MinDistance;
        Rig.Distance = target.DefaultDistance;

    }

    public void InspectPart(World world, string id) {

        foreach (var row in world.Query<Part>()) {

            if (row.C1.Craft == Target && string.Equals(row.C1.Definition.Id, id, StringComparison.OrdinalIgnoreCase)) {

                _inspection = row.Entity;
                return;

            }

        }

        throw new ArgumentException($"Camera target has no part named '{id}'.");

    }

    public void Update(World world) {

        if (_input.WasPressed(GlfwKey.Tab)) {

            Focus(_current + 1);

        }

        var orbiting = _input.IsDown(GlfwMouseButton.Right);

        _input.SetMouseCaptured(orbiting);

        if (orbiting) {

            var delta = _input.MouseDelta;

            Rig.Orbit(-delta.X * MouseSensitivity, delta.Y * MouseSensitivity);

        }

        if (_input.Scroll != 0) {

            Rig.Zoom(Math.Pow(1.0 - ZoomStep, _input.Scroll));

        }

        var entity = Target;

        if (!world.IsAlive(entity)) {

            return;

        }

        ref readonly var transform = ref world.Get<Transform>(entity);
        TargetPosition = transform.Position;

        if (world.IsAlive(_inspection) && world.Get<Part>(_inspection).Craft == entity) {

            TargetPosition = world.Get<Transform>(_inspection).Position;

        } else if (world.Store<Vessel>().Has(entity)) {

            ref readonly var vessel = ref world.Get<Vessel>(entity);
            ref readonly var root = ref world.Get<Part>(vessel.Root);
            var top = root.LocalPosition.Y + root.Definition.Length * 0.5;
            var height = Math.Max(1, top - vessel.Bottom);

            // Close inspection follows the command section; pulling back frames the whole remaining stack.
            var blend = Math.Clamp((Rig.Distance / height - 0.25) / 0.75, 0, 1);
            var midpoint = (top + vessel.Bottom) * 0.5;
            TargetPosition += transform.Rotation * new double3(0, double.Lerp(root.LocalPosition.Y, midpoint, blend * blend * (3 - 2 * blend)), 0);

        }

        Rig.Apply(Camera, TargetPosition, UpFor(world, entity));

    }

    // Around a craft, up is the local vertical of the body it orbits; around a body, it is north.
    private double3 UpFor(World world, Entity entity) {

        ref var orbit = ref world.Store<OrbitState>().TryGet(entity, out var orbiting);

        if (!orbiting) {

            return double3.UnitY;

        }

        var body = world.Get<Transform>(_scene.BodyEntity(orbit.Body)).Position;

        return TargetPosition - body;

    }

}

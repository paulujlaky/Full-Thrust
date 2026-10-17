using Brutal.GlfwApi;
using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
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

    public string TargetLabel(int index) => _targets[index].Label;

    public void Focus(int index) {

        if (_targets.Count == 0) {

            return;

        }

        _current = (index % _targets.Count + _targets.Count) % _targets.Count;

        var target = _targets[_current];

        Rig.MinDistance = target.MinDistance;
        Rig.Distance = target.DefaultDistance;

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

        TargetPosition = world.Get<Transform>(entity).Position;

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

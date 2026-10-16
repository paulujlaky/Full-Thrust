using Brutal.GlfwApi;
using FullThrust.Core.Ecs;
using FullThrust.Core.Space;
using FullThrust.Game.Platform;

namespace FullThrust.Game.Controls;

public sealed class CameraController {

    private readonly InputState _input;

    public CameraController(InputState input, Camera camera) {

        _input = input;
        Camera = camera;

    }

    public Camera Camera { get; }

    public OrbitCameraRig Rig { get; } = new();

    public Entity Target { get; set; }

    public Entity UpReference { get; set; }

    public float MouseSensitivity { get; set; } = 0.0035f;

    public float ZoomStep { get; set; } = 0.12f;

    public void Update(World world) {

        var orbiting = _input.IsDown(GlfwMouseButton.Right);

        _input.SetMouseCaptured(orbiting);

        if (orbiting) {

            var delta = _input.MouseDelta;

            Rig.Orbit(-delta.X * MouseSensitivity, delta.Y * MouseSensitivity);

        }

        if (_input.Scroll != 0) {

            Rig.Zoom(Math.Pow(1.0 - ZoomStep, _input.Scroll));

        }

        if (!world.IsAlive(Target)) {

            return;

        }

        var target = world.Get<Transform>(Target).Position;
        var up = world.IsAlive(UpReference) ? target - world.Get<Transform>(UpReference).Position : Brutal.Numerics.double3.UnitY;

        Rig.Apply(Camera, target, up);

    }

}

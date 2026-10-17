using Brutal.GlfwApi;
using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;
using FullThrust.Game.Platform;

namespace FullThrust.Game.Controls;

public sealed class FlightInputSystem : ISystem {

    private readonly InputState _input;
    private readonly FlightFocus _focus;

    public FlightInputSystem(InputState input, FlightFocus focus) {

        _input = input;
        _focus = focus;

    }

    public Entity ControlledCraft => _focus.Craft;

    public float ThrottleRate { get; set; } = 0.75f;

    public void Update(World world, in SimTime time) {

        var craft = _focus.Craft;

        if (!world.IsAlive(craft) || !world.Has<CraftControl>(craft)) {

            return;

        }

        ref var control = ref world.Get<CraftControl>(craft);

        control.Pitch = _input.Axis(GlfwKey.S, GlfwKey.W);
        control.Yaw = _input.Axis(GlfwKey.A, GlfwKey.D);
        control.Roll = _input.Axis(GlfwKey.Q, GlfwKey.E);

        control.Translation = new float3(
            _input.Axis(GlfwKey.J, GlfwKey.L),
            _input.Axis(GlfwKey.N, GlfwKey.H),
            _input.Axis(GlfwKey.I, GlfwKey.K));

        var throttle = control.Throttle + _input.Axis(GlfwKey.LeftControl, GlfwKey.LeftShift) * ThrottleRate * (float)time.RealDeltaTime;

        if (_input.WasPressed(GlfwKey.Z)) {

            throttle = 1;

        }

        if (_input.WasPressed(GlfwKey.X)) {

            throttle = 0;

        }

        control.Throttle = Math.Clamp(throttle, 0, 1);

        if (_input.WasPressed(GlfwKey.T)) {

            control.Sas = !control.Sas;

        }

        if (_input.WasPressed(GlfwKey.R)) {

            control.Rcs = !control.Rcs;

        }

        if (_input.WasPressed(GlfwKey.Space) && world.Has<CraftStaging>(craft)) {

            world.Get<CraftStaging>(craft).Requested = true;

        }

    }

}

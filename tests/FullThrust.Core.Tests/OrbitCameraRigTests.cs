using Brutal.Numerics;
using FullThrust.Core.Space;

namespace FullThrust.Core.Tests;

public class OrbitCameraRigTests {

    [Fact]
    public void CameraLooksAtTargetFromConfiguredDistance() {

        var rig = new OrbitCameraRig { Distance = 50, Yaw = 1.0, Pitch = 0.4 };
        var camera = new Camera();
        var target = new double3(6_471_000, 0, 0);

        rig.Apply(camera, target, target);

        var toTarget = double3.Normalize(target - camera.Position);

        Assert.Equal(50, double3.Distance(camera.Position, target), 1e-6);
        Assert.Equal(1.0, double3.Dot(toTarget, camera.Forward), 1e-9);
        Assert.True(double3.Dot(camera.Up, double3.UnitX) > 0);

    }

    [Fact]
    public void LookRotationProducesOrthonormalBasis() {

        var forward = double3.Normalize(new double3(1, -0.5, 0.25));
        var rotation = OrbitCameraRig.LookRotation(forward, double3.UnitY);

        var mappedForward = rotation * new double3(0, 0, -1);
        var mappedUp = rotation * double3.UnitY;

        Assert.Equal(1.0, double3.Dot(mappedForward, forward), 1e-9);
        Assert.Equal(0.0, double3.Dot(mappedForward, mappedUp), 1e-9);

    }

    [Fact]
    public void ZoomIsClamped() {

        var rig = new OrbitCameraRig { Distance = 10, MinDistance = 5, MaxDistance = 20 };

        rig.Zoom(0.1);
        Assert.Equal(5, rig.Distance);

        rig.Zoom(100);
        Assert.Equal(20, rig.Distance);

    }

}

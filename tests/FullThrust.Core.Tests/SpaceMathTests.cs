using Brutal.Numerics;
using FullThrust.Core.Space;

namespace FullThrust.Core.Tests;

public class SpaceMathTests {

    [Fact]
    public void ReverseZProjectionMapsNearToOneAndFarTowardZero() {

        var projection = SpaceMath.PerspectiveReverseZ(MathF.PI / 2, 1, 0.1f);

        var near = new float4(0, 0, -0.1f, 1) * projection;
        var far = new float4(0, 0, -1e7f, 1) * projection;

        Assert.Equal(1.0f, near.Z / near.W, 1e-6f);
        Assert.True(far.Z / far.W is > 0 and < 1e-7f);

    }

    [Fact]
    public void ProjectionFlipsYForVulkan() {

        var projection = SpaceMath.PerspectiveReverseZ(MathF.PI / 2, 1, 0.1f);

        var up = new float4(0, 1, -1, 1) * projection;

        Assert.True(up.Y / up.W < 0);

    }

    [Fact]
    public void IntegratingAngularVelocityRotatesAboutAxis() {

        var rotation = SpaceMath.Integrate(doubleQuat.Identity, new double3(0, Math.PI / 2, 0), 1.0);
        var forward = rotation * new double3(0, 0, -1);

        Assert.Equal(-1.0, forward.X, 1e-9);
        Assert.Equal(0.0, forward.Z, 1e-9);

    }

}

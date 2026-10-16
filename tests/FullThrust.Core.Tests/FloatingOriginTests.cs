using Brutal.Numerics;
using FullThrust.Core.Space;

namespace FullThrust.Core.Tests;

public class FloatingOriginTests {

    private sealed class RecordingListener : IOriginShiftListener {

        public double3 TotalDelta;

        public int Calls;

        public void OnOriginShifted(double3 delta, double3 newOrigin) {

            TotalDelta += delta;
            Calls++;

        }

    }

    [Fact]
    public void DoesNotShiftWithinRecenterDistance() {

        var origin = new FloatingOrigin { RecenterDistance = 1000 };

        Assert.False(origin.Update(new double3(999, 0, 0)));
        Assert.Equal(double3.Zero, origin.Origin);

    }

    [Fact]
    public void ShiftsToFocusAndNotifiesListeners() {

        var origin = new FloatingOrigin { RecenterDistance = 1000 };
        var listener = new RecordingListener();

        origin.AddListener(listener);

        var focus = new double3(6_471_000, 12, -5);

        Assert.True(origin.Update(focus));
        Assert.Equal(focus, origin.Origin);
        Assert.Equal(focus, listener.TotalDelta);
        Assert.Equal(1, listener.Calls);
        Assert.Equal(1, origin.ShiftCount);

    }

    [Fact]
    public void LocalCoordinatesKeepCentimetrePrecisionFarFromWorldOrigin() {

        var origin = new FloatingOrigin { RecenterDistance = 1000 };
        var craft = new double3(6_471_000.0, 1_234_567.0, 0.0);

        origin.Update(craft);

        var offset = new double3(0.013, 0.027, -0.004);
        var local = origin.ToLocal(craft + offset);

        Assert.Equal(0.013f, local.X, 1e-5f);
        Assert.Equal(0.027f, local.Y, 1e-5f);
        Assert.Equal(-0.004f, local.Z, 1e-5f);

        var withoutOrigin = SpaceMath.ToFloat(craft + offset) - SpaceMath.ToFloat(craft);

        Assert.NotEqual(0.013f, withoutOrigin.X, 1e-3f);

    }

    [Fact]
    public void RoundTripsBetweenWorldAndLocal() {

        var origin = new FloatingOrigin();

        origin.ShiftTo(new double3(1e9, -2e9, 3e9));

        var world = new double3(1e9 + 10.5, -2e9 - 3.25, 3e9 + 0.125);
        var back = origin.ToWorld(origin.ToLocal(world));

        Assert.Equal(world.X, back.X, 1e-6);
        Assert.Equal(world.Y, back.Y, 1e-6);
        Assert.Equal(world.Z, back.Z, 1e-6);

    }

    [Fact]
    public void DisablingReturnsOriginToZero() {

        var origin = new FloatingOrigin();

        origin.ShiftTo(new double3(5000, 0, 0));
        origin.Enabled = false;

        Assert.True(origin.Update(new double3(5000, 0, 0)));
        Assert.Equal(double3.Zero, origin.Origin);
        Assert.False(origin.Update(new double3(1e7, 0, 0)));

    }

}

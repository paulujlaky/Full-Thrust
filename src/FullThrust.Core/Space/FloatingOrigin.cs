using Brutal.Numerics;

namespace FullThrust.Core.Space;

public interface IOriginShiftListener {

    void OnOriginShifted(double3 delta, double3 newOrigin);

}

public sealed class FloatingOrigin {

    private readonly List<IOriginShiftListener> _listeners = [];

    public double3 Origin { get; private set; }

    public double RecenterDistance { get; set; } = 1000.0;

    public bool Enabled { get; set; } = true;

    public long ShiftCount { get; private set; }

    public void AddListener(IOriginShiftListener listener) => _listeners.Add(listener);

    public bool RemoveListener(IOriginShiftListener listener) => _listeners.Remove(listener);

    public bool Update(double3 focus) {

        if (!Enabled) {

            if (Origin == double3.Zero) {

                return false;

            }

            ShiftTo(double3.Zero);

            return true;

        }

        if (double3.DistanceSquared(focus, Origin) <= RecenterDistance * RecenterDistance) {

            return false;

        }

        ShiftTo(focus);

        return true;

    }

    public void ShiftTo(double3 newOrigin) {

        var delta = newOrigin - Origin;

        Origin = newOrigin;
        ShiftCount++;

        for (var i = 0; i < _listeners.Count; i++) {

            _listeners[i].OnOriginShifted(delta, newOrigin);

        }

    }

    public double3 ToLocalPrecise(double3 world) => world - Origin;

    public float3 ToLocal(double3 world) => SpaceMath.ToFloat(world - Origin);

    public double3 ToWorld(float3 local) => Origin + SpaceMath.ToDouble(local);

}

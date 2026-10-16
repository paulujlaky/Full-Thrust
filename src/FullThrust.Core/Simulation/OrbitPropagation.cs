using Brutal.Numerics;

namespace FullThrust.Core.Simulation;

public struct StateVector {

    public double3 Position;
    public double3 Velocity;

    public StateVector(double3 position, double3 velocity) {

        Position = position;
        Velocity = velocity;

    }

}

public interface IOrbitPropagator {

    StateVector Propagate(in StateVector state, double gravitationalParameter, double deltaTime);

}

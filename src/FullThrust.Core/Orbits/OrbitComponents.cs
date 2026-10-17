using FullThrust.Core.Simulation;

namespace FullThrust.Core.Orbits;

public struct OrbitState {

    public int Body;

    // Body-relative state the conic is propagated from.
    public StateVector Epoch;
    public double EpochTime;

    public StateVector Current;
    public double CurrentTime;

    // Bumped whenever the conic changes, so cached predictions know to rebuild.
    public int Version;

    public static OrbitState Create(int body, in StateVector state, double universalTime) {

        return new OrbitState {

            Body = body,
            Epoch = state,
            EpochTime = universalTime,
            Current = state,
            CurrentTime = universalTime,

        };

    }

    public void Rebase(int body, in StateVector state, double universalTime) {

        Body = body;
        Epoch = state;
        EpochTime = universalTime;
        Current = state;
        CurrentTime = universalTime;
        Version++;

    }

}

public struct CelestialBodyRef {

    public int Index;

    public CelestialBodyRef(int index) {

        Index = index;

    }

}

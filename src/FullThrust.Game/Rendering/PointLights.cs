using Brutal.Numerics;

namespace FullThrust.Game.Rendering;

// Point lights such as plumes and reentry plasma, added while volumes are collected and written to the next frame's uniforms.
public sealed class PointLights {

    private readonly float4[] _positions = new float4[LightVectors.MaxPointLights];
    private readonly float4[] _intensities = new float4[LightVectors.MaxPointLights];

    public int Count { get; private set; }

    // Position is render-local, radius softens the falloff near the source, intensity is in candela; false once full.
    public bool Add(float3 position, float radius, float3 intensity) {

        if (Count >= LightVectors.MaxPointLights) {

            return false;

        }

        _positions[Count] = new float4(position, radius);
        _intensities[Count] = new float4(intensity, 0);
        Count++;

        return true;

    }

    public void Fill(ref FrameUniforms uniforms, bool enabled) {

        var count = enabled ? Count : 0;

        for (var i = 0; i < count; i++) {

            uniforms.LightPositions[i] = _positions[i];
            uniforms.LightIntensities[i] = _intensities[i];

        }

        uniforms.LightCount = new float4(count, 0, 0, 0);
        Count = 0;

    }

}

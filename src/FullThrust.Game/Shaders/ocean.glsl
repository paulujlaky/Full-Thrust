// Open-ocean surface: deep-water waves in the body-fixed frame, animated by the dispersion relation.
// Waves too small to resolve at this distance are folded into roughness, so the sun glint survives from orbit.

const int WaveCount = 10;

// Slope of each wave (amplitude times wavenumber); the sum of their variances matches Cox-Munk at a light breeze.
const float WaveSteepness = 0.075;

const vec3 WaveDirections[WaveCount] = vec3[](

    vec3(0.80, 0.10, 0.59), vec3(0.62, -0.35, 0.70), vec3(0.95, 0.22, -0.20), vec3(0.41, 0.60, 0.69), vec3(0.70, -0.62, 0.35),
    vec3(-0.15, 0.40, 0.90), vec3(0.88, -0.10, 0.46), vec3(0.30, 0.86, -0.41), vec3(0.58, 0.31, -0.75), vec3(-0.52, 0.79, 0.32)

);

struct OceanSurface {

    vec3 normal;
    float roughness;

};

OceanSurface Ocean(vec3 position, vec3 up, float time, float metresPerPixel) {

    vec3 slope = vec3(0.0);
    float lostVariance = 0.0;
    float wavelength = 60.0;

    for (int i = 0; i < WaveCount; i++) {

        float k = 6.2831853 / wavelength;
        float omega = sqrt(9.81 * k);
        vec3 direction = normalize(WaveDirections[i]);

        // A wave needs about four pixels per wavelength to be drawn; below that only its slope variance remains.
        float resolved = 1.0 - smoothstep(0.15, 0.3, metresPerPixel / wavelength);
        float phase = dot(position, direction) * k - omega * time + float(i) * 1.7;

        slope += direction * (WaveSteepness * cos(phase) * resolved);
        lostVariance += 0.5 * WaveSteepness * WaveSteepness * (1.0 - resolved);

        wavelength *= 0.62;

    }

    slope -= up * dot(slope, up);

    OceanSurface o;

    o.normal = normalize(up - slope);

    // GGX alpha squared grows by the unresolved slope variance; roughness is the square root of alpha.
    o.roughness = pow(0.04 * 0.04 + lostVariance, 0.25);

    return o;

}

// Breaking water along the shore: depth is metres below sea level, so it shows only in the shallows.
float ShoreFoam(vec3 position, float depth, float time) {

    float band = 1.0 - smoothstep(0.0, 1.2, depth);
    float surge = 0.5 + 0.5 * sin(time * 0.9 - depth * 3.0 + dot(position, vec3(0.031, 0.017, 0.023)));

    return band * smoothstep(0.35, 0.9, surge * band);

}

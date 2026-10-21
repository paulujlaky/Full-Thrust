// Deep-water waves; unresolved ones fold into roughness so the glint survives from orbit.

// Trains down to about 2 m; shorter capillary ripples come from noise, since crossing sinusoids there read as a lattice.
const int WaveCount = 12;

// Slope of each wave (amplitude times wavenumber); the sum of their variances matches Cox-Munk at a light breeze.
const float WaveSteepness = 0.069;

// Body-fixed wind the waves run with; shorter waves spread further from it.
const vec3 WindDirection = vec3(0.80, 0.31, 0.52);

vec3 WaveRandom(float i) {

    return fract(sin(vec3(i * 12.9898, i * 78.233, i * 37.719)) * 43758.5453);

}

struct OceanSurface {

    vec3 normal;
    float roughness;

};

OceanSurface Ocean(vec3 position, vec3 up, float time, float metresPerPixel) {

    vec3 slope = vec3(0.0);
    float lostVariance = 0.0;
    float wavelength = 80.0;

    for (int i = 0; i < WaveCount; i++) {

        vec3 random = WaveRandom(float(i) + 1.0);
        float span = wavelength * (0.85 + 0.3 * random.x);
        float k = 6.2831853 / span;
        float omega = sqrt(9.81 * k);
        float spread = 0.4 + 1.2 * float(i) / float(WaveCount);
        vec3 direction = normalize(normalize(WindDirection) + (random * 2.0 - 1.0) * spread);

        // A wave needs about four pixels per wavelength to be drawn; below that only its slope variance remains.
        float resolved = 1.0 - smoothstep(0.15, 0.3, metresPerPixel / span);
        float phase = dot(position, direction) * k - omega * time + random.y * 6.2831853;

        slope += direction * (WaveSteepness * cos(phase) * resolved);
        lostVariance += 0.5 * WaveSteepness * WaveSteepness * (1.0 - resolved);

        wavelength *= 0.72;

    }

    // Wind ripples from drifting noise break up what regularity the wave trains leave.
    float rippleResolved = 1.0 - smoothstep(0.1, 0.4, metresPerPixel);

    if (rippleResolved > 0.0) {

        vec3 drift = vec3(time * 0.04, 0.0, time * 0.03);
        // Rotated off the noise texture's axes, at scales that share no period, so its tiling never lines up.
        mat3 turn = mat3(0.78, 0.42, -0.46, -0.31, 0.90, 0.30, 0.54, -0.11, 0.83);
        vec3 fine = texture(cloudNoise, turn * position * (1.0 / 7.3) + drift).gba - 0.5;
        vec3 finer = texture(cloudNoise, position * (turn * vec3(1.0 / 2.9)) - drift * 2.0).gba - 0.5;

        slope += (fine * 0.12 + finer * 0.05) * rippleResolved;

    }

    lostVariance += 0.004 * (1.0 - rippleResolved);
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

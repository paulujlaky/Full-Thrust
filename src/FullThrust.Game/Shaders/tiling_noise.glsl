// Periodic gradient and cellular noise for tileable 3D textures; coordinates are in cells, period in cells.

#include "remap.glsl"

uvec3 NoiseHash(uvec3 v) {

    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;

    return v;

}

vec3 NoiseRandom(ivec3 cell, int period) {

    uvec3 wrapped = uvec3((cell % period + period) % period);

    return vec3(NoiseHash(wrapped) & 0xFFFFu) / 65535.0;

}

float PeriodicPerlin(vec3 p, int period) {

    ivec3 i = ivec3(floor(p));
    vec3 f = fract(p);
    vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float result = 0.0;

    for (int z = 0; z < 2; z++) {

        for (int y = 0; y < 2; y++) {

            for (int x = 0; x < 2; x++) {

                ivec3 corner = ivec3(x, y, z);
                vec3 gradient = normalize(NoiseRandom(i + corner, period) * 2.0 - 1.0 + 1e-4);
                float d = dot(gradient, f - vec3(corner));
                vec3 w = mix(1.0 - u, u, vec3(corner));

                result += d * w.x * w.y * w.z;

            }

        }

    }

    return result * 0.5 + 0.5;

}

// 1 at feature points, falling to 0 a cell away.
float PeriodicWorley(vec3 p, int period) {

    ivec3 i = ivec3(floor(p));
    vec3 f = fract(p);
    float nearest = 1.0;

    for (int z = -1; z <= 1; z++) {

        for (int y = -1; y <= 1; y++) {

            for (int x = -1; x <= 1; x++) {

                ivec3 offset = ivec3(x, y, z);
                vec3 feature = vec3(offset) + NoiseRandom(i + offset, period);

                nearest = min(nearest, length(feature - f));

            }

        }

    }

    return 1.0 - clamp(nearest, 0.0, 1.0);

}

float WorleyFbm(vec3 uvw, int frequency) {

    return PeriodicWorley(uvw * frequency, frequency) * 0.625 + PeriodicWorley(uvw * frequency * 2, frequency * 2) * 0.25 + PeriodicWorley(uvw * frequency * 4, frequency * 4) * 0.125;

}

float PerlinFbm(vec3 uvw, int frequency) {

    float sum = 0.0;
    float amplitude = 0.5;

    for (int octave = 0; octave < 5; octave++) {

        sum += PeriodicPerlin(uvw * frequency, frequency) * amplitude;
        frequency *= 2;
        amplitude *= 0.5;

    }

    return sum / 0.96875;

}

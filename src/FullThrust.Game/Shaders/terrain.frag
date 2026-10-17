#version 450

#include "common.glsl"
#include "terrain_push.glsl"

layout(location = 0) in vec3 vWorldPosition;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec4 vSurface;
layout(location = 3) in vec3 vDetail;
layout(location = 4) in float vHeight;
layout(location = 5) in vec3 vUp;

layout(location = 0) out vec4 outColor;

// Must match TerrainRenderer.DetailPeriod: detail coordinates wrap there, so the noise repeats on the same period.
const float DetailPeriod = 65536.0;

// PCG-style integer hash; sine hashes lose precision and show grid artefacts at large cell indices.
float Hash(vec3 cell, float period) {

    uvec3 v = uvec3(ivec3(mod(cell, vec3(period))));

    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;

    return float(v.x & 0xFFFFFFu) / 16777215.0;

}

// Value noise with an integer period that divides DetailPeriod, so it tiles seamlessly.
float ValueNoise(vec3 p, float period) {

    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);

    float n000 = Hash(i, period);
    float n100 = Hash(i + vec3(1, 0, 0), period);
    float n010 = Hash(i + vec3(0, 1, 0), period);
    float n110 = Hash(i + vec3(1, 1, 0), period);
    float n001 = Hash(i + vec3(0, 0, 1), period);
    float n101 = Hash(i + vec3(1, 0, 1), period);
    float n011 = Hash(i + vec3(0, 1, 1), period);
    float n111 = Hash(i + vec3(1, 1, 1), period);

    return mix(mix(mix(n000, n100, u.x), mix(n010, n110, u.x), u.y), mix(mix(n001, n101, u.x), mix(n011, n111, u.x), u.y), u.z);

}

// Ground variation below the mesh resolution, from 128 m down to 0.5 m; each octave fades once it is under a pixel.
float Detail(vec3 p) {

    float footprint = length(fwidth(p));
    float sum = 0.0;
    float amplitude = 0.5;
    float scale = 1.0 / 128.0;

    for (int octave = 0; octave < 9; octave++) {

        float fade = 1.0 - smoothstep(0.3, 1.0, footprint * scale);

        sum += (ValueNoise(p * scale, DetailPeriod * scale) - 0.5) * amplitude * fade;
        amplitude *= 0.75;
        scale *= 2.0;

    }

    return sum;

}

vec3 SrgbToLinear(vec3 c) {

    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));

}

void main() {

    vec3 n = normalize(vNormal);
    vec3 v = normalize(frame.cameraPosition.xyz - vWorldPosition);

    Surface s;

    s.albedo = SrgbToLinear(vSurface.rgb);
    s.metallic = 0.0;
    s.roughness = vSurface.a;
    s.occlusion = 1.0;
    s.emissive = vec3(0.0);

    float d = Detail(vDetail);

    s.albedo *= 1.0 + 0.8 * d;
    s.roughness = clamp(s.roughness + 0.15 * d, 0.3, 1.0);

    // The shoreline follows the interpolated signed height, so coasts are smooth between vertices.
    if (pc.deepWater.w > 0.5 && vHeight < 0.0) {

        float depth = -vHeight;

        s.albedo = mix(pc.shallowWater.rgb, pc.deepWater.rgb, smoothstep(0.0, 250.0, depth));
        s.roughness = 0.07;
        n = normalize(vUp);

    }

    if (pc.detail.w > 0.0) {

        float level = pc.detail.w - 1.0;

        s.albedo = 0.5 + 0.5 * cos(6.2831 * (level / 7.0 + vec3(0.0, 0.33, 0.67)));

    }

    outColor = vec4(Expose(ShadeSurface(s, n, v, 1.0)), 1.0);

}

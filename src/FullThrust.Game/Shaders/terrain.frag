#version 450

#include "common.glsl"
#include "terrain_push.glsl"
#include "terrain_materials.glsl"

layout(location = 0) in vec3 vWorldPosition;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec4 vSurface;
layout(location = 3) in vec3 vDetail;
layout(location = 4) in float vHeight;
layout(location = 5) in vec3 vUp;
layout(location = 6) in vec3 vBodyNormal;
layout(location = 7) in vec4 vWeights0;
layout(location = 8) in vec4 vWeights1;

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

const int Picks = 3;

void main() {

    TerrainBody body = terrain.bodies[pc.body.x];

    vec3 n = normalize(vNormal);
    vec3 v = normalize(frame.cameraPosition.xyz - vWorldPosition);

    vec3 macro = SrgbToLinear(vSurface.rgb);
    float metresPerPixel = length(fwidth(vDetail));

    float weights[8] = float[](vWeights0.x, vWeights0.y, vWeights0.z, vWeights0.w, vWeights1.x, vWeights1.y, vWeights1.z, vWeights1.w);
    int layers[8] = int[](body.layers0.x, body.layers0.y, body.layers0.z, body.layers0.w, body.layers1.x, body.layers1.y, body.layers1.z, body.layers1.w);
    float scales[8] = float[](body.tileScale0.x, body.tileScale0.y, body.tileScale0.z, body.tileScale0.w, body.tileScale1.x, body.tileScale1.y, body.tileScale1.z, body.tileScale1.w);

    // The heaviest channels that have a detail material; the rest keep the palette colour.
    int pick[Picks] = int[](-1, -1, -1);
    float pickWeight[Picks] = float[](0.0, 0.0, 0.0);

    for (int c = 0; c < 8; c++) {

        float w = weights[c];

        if (layers[c] < 0 || w <= pickWeight[Picks - 1]) {

            continue;

        }

        int slot = Picks - 1;

        while (slot > 0 && w > pickWeight[slot - 1]) {

            pick[slot] = pick[slot - 1];
            pickWeight[slot] = pickWeight[slot - 1];
            slot--;

        }

        pick[slot] = c;
        pickWeight[slot] = w;

    }

    vec3 bodyNormal = normalize(vBodyNormal);
    vec3 blend = pow(abs(bodyNormal), vec3(4.0));

    blend /= blend.x + blend.y + blend.z;

    vec3 albedo = vec3(0.0);
    vec3 normalOffset = vec3(0.0);
    float roughness = 0.0;
    float occlusion = 0.0;
    float covered = 0.0;
    float fade = 0.0;

    if (pick[0] >= 0 && TileFade(metresPerPixel, scales[pick[0]] * 0.125) > 0.0) {

        DetailSample samples[Picks];
        float heightWeight[Picks];
        float top = 0.0;

        for (int i = 0; i < Picks; i++) {

            heightWeight[i] = 0.0;

            if (pick[i] < 0) {

                continue;

            }

            float levelFade;

            samples[i] = SampleLayered(layers[pick[i]], vDetail, blend, scales[pick[i]], metresPerPixel, levelFade);
            fade = max(fade, levelFade);
            // Heights are centred on each texture's mean so no material wins transitions on average.
            // Only the fine level sharpens transitions: coarse heights would print the tile pattern across the landscape.
            float contrast = 0.6 * TileFade(metresPerPixel, scales[pick[i]]);
            float breakup = (ValueNoise(vDetail * (1.0 / 32.0) + float(pick[i]) * 17.0, DetailPeriod / 32.0) - 0.5) * 0.5;

            heightWeight[i] = pickWeight[i] + (samples[i].height - body.inverseMean[pick[i]].w) * contrast + breakup * contrast;
            top = max(top, heightWeight[i]);
            covered += pickWeight[i];

        }

        float total = 0.0;

        // Height blending: the material standing proud of the others wins near a transition.
        for (int i = 0; i < Picks; i++) {

            heightWeight[i] = pick[i] < 0 ? 0.0 : max(heightWeight[i] - top + 0.2, 0.0);
            total += heightWeight[i];

        }

        for (int i = 0; i < Picks; i++) {

            if (heightWeight[i] <= 0.0) {

                continue;

            }

            float w = heightWeight[i] / total;

            // Texture brightness over the palette hue, with a little of the texture's own colour.
            vec4 palette = body.palette[pick[i]];
            float brightness = dot(samples[i].albedo, vec3(0.2126, 0.7152, 0.0722)) * palette.w;
            vec3 coloured = palette.rgb * samples[i].albedo * body.inverseMean[pick[i]].rgb;

            albedo += w * mix(palette.rgb * brightness, coloured, 0.3);
            normalOffset += w * samples[i].normal;
            roughness += w * samples[i].roughness;
            occlusion += w * samples[i].occlusion;

        }

    }

    float detail = fade * clamp(covered, 0.0, 1.0);

    Surface s;

    s.albedo = mix(macro, albedo, detail);
    s.metallic = 0.0;
    s.roughness = mix(vSurface.a, roughness, detail);
    s.occlusion = mix(1.0, occlusion, detail);
    s.emissive = vec3(0.0);

    if (detail > 0.0) {

        n = normalize(mat3(pc.model) * normalize(bodyNormal + normalOffset * detail));

    }

    float d = Detail(vDetail);

    s.albedo *= 1.0 + 0.6 * d;
    s.roughness = clamp(s.roughness + 0.1 * d, 0.3, 1.0);

    // The shoreline follows the interpolated signed height, so coasts are smooth between vertices.
    if (body.deepWater.w > 0.5 && vHeight < 0.0) {

        float depth = -vHeight;

        s.albedo = mix(body.shallowWater.rgb, body.deepWater.rgb, smoothstep(0.0, 250.0, depth));
        s.roughness = 0.07;
        s.occlusion = 1.0;
        n = normalize(vUp);

    }

    if (pc.detail.w > 0.0) {

        float level = pc.detail.w - 1.0;

        s.albedo = 0.5 + 0.5 * cos(6.2831 * (level / 7.0 + vec3(0.0, 0.33, 0.67)));

    }

    s.occlusion *= ScreenOcclusion(gl_FragCoord.xy, s.albedo);
    s.albedo *= CascadeTint(vWorldPosition);

    outColor = SceneOutput(ShadeSurface(s, n, v, SunShadow(vWorldPosition, normalize(vNormal))), gl_FragCoord.xy);

}

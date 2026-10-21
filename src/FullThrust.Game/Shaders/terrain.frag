#version 450

#include "common.glsl"
#include "terrain_push.glsl"
#include "terrain_materials.glsl"
#include "ocean.glsl"

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

// Ground variation below the mesh resolution, from 128 m down to 4 m where detail textures take over; octaves fade under a pixel.
float Detail(vec3 p) {

    float footprint = length(fwidth(p));
    float sum = 0.0;
    float amplitude = 0.5;
    float scale = 1.0 / 128.0;

    for (int octave = 0; octave < 6; octave++) {

        float fade = 1.0 - smoothstep(0.3, 1.0, footprint * scale);

        // Octaves only get finer, so once one is below a pixel the rest are too.
        if (fade <= 0.0) {

            break;

        }

        sum += (ValueNoise(p * scale, DetailPeriod * scale) - 0.5) * amplitude * fade;
        amplitude *= 0.75;
        scale *= 2.0;

    }

    return sum;

}

vec3 SrgbToLinear(vec3 c) {

    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));

}

// Turns the city lattice against the town lattice, so their cells never line up into rows.
const mat3 Tilt = mat3(0.36, 0.48, -0.8, -0.8, 0.6, 0.0, 0.48, 0.64, 0.6);

// PCG4D (Jarzynski and Olano 2020): jitter and a lottery value for one lattice cell.
vec4 CellRandom(vec3 cell, uint seed) {

    uvec4 v = uvec4(uvec3(ivec3(cell)), seed);

    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.w;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v.w += v.y * v.z;
    v ^= v >> 16u;
    v.x += v.y * v.w;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v.w += v.y * v.z;

    return vec4(v & 0xFFFFFFu) / 16777215.0;

}

// Mean light of Places per unit of chance: the integral of one place's profile over the rank-size law.
const float PlacesMean = 0.010;

// Places on a jittered lattice, q in cells on a sphere: sizes follow a rank-size law, so most are villages and only a few grow large.
float Places(vec3 q, float footprint, float chance, float warp, float ragged, uint seed) {

    vec3 base = floor(q - 0.5);
    float sphere = length(q);
    float light = 0.0;

    for (int i = 0; i < 8; i++) {

        vec3 cell = base + vec3(i & 1, (i >> 1) & 1, i >> 2);
        vec4 r = CellRandom(cell, seed);
        vec3 centre = normalize(cell + r.xyz) * sphere;

        // A place drops onto the surface and belongs to the cell it lands in, so every cell the surface crosses can hold one.
        if (r.w >= chance || floor(centre) != cell) {

            continue;

        }

        // Zipf's law: the share of places larger than a size falls as one over that size.
        float size = 0.03 / max(r.w / chance, 0.03);
        float radius = 0.3 * sqrt(size);

        // Kept within half a cell even when warped, so the eight nearest cells hold every place that reaches this pixel.
        float spread = min(sqrt(radius * radius + footprint * footprint), 0.3);
        float t = distance(q, centre) * (0.6 + 0.8 * warp) / spread;

        // A dense core inside a dimmer sprawl whose edge frays into fingers and gaps.
        float profile = 0.55 * exp(-8.0 * t * t) + 0.45 * (1.0 - smoothstep(0.35, 0.75, t + (ragged - 0.5) * 0.9));

        light += (0.3 + 0.7 * size) * radius * radius / (spread * spread) * profile * 2.0;

    }

    // Once places shrink below a pixel their light averages out rather than sparkling.
    return mix(light, chance * PlacesMean, smoothstep(0.2, 0.37, footprint));

}

// Night-side city lights in cd/m2: settled regions from untiled noise, villages and towns on one lattice, cities on a coarser tilted one.
vec3 CityLights(vec3 direction, vec3 detail, float height, float metresPerPixel, float radiansPerPixel, vec4 settlements, float luminance) {

    // Towns favour wet lowlands and coasts, and shun ice, bare rock and the high deserts.
    float habitable = clamp(vWeights0.x + 0.3 * vWeights0.y + 0.8 * vWeights0.w - vWeights1.y - 0.5 * vWeights1.x, 0.0, 1.0);
    float lowland = exp(-max(height, 0.0) / 900.0);
    float temperate = 1.0 - smoothstep(0.82, 0.92, abs(direction.y));
    float livable = habitable * lowland * temperate;

    if (livable <= 0.0) {

        return vec3(0.0);

    }

    vec3 d = direction * 3.0;
    float region = ValueNoise(d, DetailPeriod) * 0.5 + ValueNoise(Tilt * d * 2.7, DetailPeriod) * 0.3 + ValueNoise(d * 7.9 + 31.0, DetailPeriod) * 0.2;

    // The fbm's spread is about 0.1, so this threshold leaves roughly the settled share above it.
    float threshold = 0.5 + (0.5 - settlements.z) * 0.26;
    float settled = smoothstep(threshold - 0.06, threshold + 0.06, region) * livable;

    if (settled <= 0.0) {

        return vec3(0.0);

    }

    uint seed = uint(settlements.w);
    vec3 towns = direction * settlements.x;
    vec3 cities = Tilt * direction * settlements.y;

    float townWarp = ValueNoise(towns * 1.7, DetailPeriod) * 0.6 + ValueNoise(towns * 6.3 + 11.0, DetailPeriod) * 0.4;
    float cityWarp = ValueNoise(cities * 2.3, DetailPeriod) * 0.5 + ValueNoise(cities * 7.1 + 17.0, DetailPeriod) * 0.3 + ValueNoise(cities * 23.0 + 3.0, DetailPeriod) * 0.2;

    // Frayed edges down to a couple of hundred metres, settling to their mean once a pixel spans them.
    float ragged = 0.5;

    if (metresPerPixel < 1200.0) {

        ragged = mix(ValueNoise(towns * 19.0, DetailPeriod) * 0.6 + ValueNoise(towns * 47.0 + 7.0, DetailPeriod) * 0.4, 0.5, smoothstep(150.0, 1200.0, metresPerPixel));

    }

    // Places crowd into corridors a few hundred kilometres long, leaving thin country between them.
    float crowding = settled * (0.3 + 1.4 * ValueNoise(Tilt * direction * 24.0 + 5.0, DetailPeriod));

    float places = Places(towns, radiansPerPixel * settlements.x, min(0.7 * crowding, 1.0), townWarp, ragged, seed) + 2.5 * Places(cities, radiansPerPixel * settlements.y, 0.5 * crowding, cityWarp, ragged, seed + 7919u);

    // Street grain averages to its mean once a pixel covers a few blocks, so cities do not sparkle from orbit.
    float grain = texture(cloudNoise, detail * (1.0 / 2048.0) + vec3(0.71, 0.37, 0.19)).a;
    float streets = mix(smoothstep(0.35, 0.75, grain) * 2.2, 1.0, smoothstep(60.0, 400.0, metresPerPixel));
    float districts = mix(0.3 + 1.4 * texture(cloudNoise, detail * (1.0 / 16384.0) + vec3(0.23, 0.91, 0.57)).b, 1.0, smoothstep(300.0, 2500.0, metresPerPixel));

    float density = places * streets * districts;
    vec3 sodium = vec3(1.0, 0.62, 0.3);

    return mix(sodium, vec3(1.0, 0.9, 0.75), smoothstep(0.4, 1.2, density)) * density * luminance;

}

const int Picks = 3;

void main() {

    TerrainBody body = terrain.bodies[pc.body.x];

    vec3 n = normalize(vNormal);
    vec3 v = normalize(frame.cameraPosition.xyz - vWorldPosition);

    vec3 macro = SrgbToLinear(vSurface.rgb);
    // Derivatives are taken here, in uniform control flow, for the texture fetches inside branches below.
    vec3 detailDx = dFdx(vDetail);
    vec3 detailDy = dFdy(vDetail);
    float metresPerPixel = length(abs(detailDx) + abs(detailDy));
    vec3 bodyUp = normalize(transpose(mat3(pc.model)) * normalize(vUp));
    float radiansPerPixel = length(fwidth(bodyUp));

    // Smooth index picking which shifted copy of each tile to show, so tiling never forms a lattice.
    float variant = ValueNoise(vDetail * (1.0 / 64.0), DetailPeriod / 64.0) * 8.0;

    float weights[8] = float[](vWeights0.x, vWeights0.y, vWeights0.z, vWeights0.w, vWeights1.x, vWeights1.y, vWeights1.z, vWeights1.w);
    int layers[8] = int[](body.layers0.x, body.layers0.y, body.layers0.z, body.layers0.w, body.layers1.x, body.layers1.y, body.layers1.z, body.layers1.w);
    float scales[8] = float[](body.tileScale0.x, body.tileScale0.y, body.tileScale0.z, body.tileScale0.w, body.tileScale1.x, body.tileScale1.y, body.tileScale1.z, body.tileScale1.w);

    // The heaviest channels that have a detail material; the rest keep the palette colour.
    int pick[Picks] = int[](-1, -1, -1);
    float pickWeight[Picks] = float[](0.0, 0.0, 0.0);

    for (int c = 0; c < 8; c++) {

        float w = weights[c];

        if (layers[c] < 0 || w < 0.04 || w <= pickWeight[Picks - 1]) {

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

            samples[i] = SampleLayered(layers[pick[i]], vDetail, detailDx, detailDy, blend, scales[pick[i]], metresPerPixel, variant, levelFade);
            fade = max(fade, levelFade);
            // Mean-centred heights sharpen transitions, at the fine level only so tiles never print.
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
    s.reflectance = 0.04;
    s.geometricNormal = normalize(vNormal);

    // Grass, stones and soil hide the ground from a light skimming in below about ten degrees.
    s.relief = 0.18;

    if (detail > 0.0) {

        n = normalize(mat3(pc.model) * normalize(bodyNormal + normalOffset * detail));

    }

    float d = Detail(vDetail);

    s.albedo *= 1.0 + 0.6 * d;
    s.roughness = clamp(s.roughness + 0.1 * d, 0.3, 1.0);

    bool ocean = body.deepWater.w > 0.5;
    float time = frame.exposure.w;

    // Wet ground just above the waterline is darker and glossier.
    if (ocean && vHeight >= 0.0 && vHeight < 1.5) {

        float wet = 1.0 - smoothstep(0.0, 1.5, vHeight);

        s.albedo *= 1.0 - 0.45 * wet;
        s.roughness = mix(s.roughness, 0.25, wet);

    }

    // The shoreline follows the interpolated signed height, so coasts are smooth between vertices.
    if (ocean && vHeight < 0.0) {

        float depth = -vHeight;
        mat3 toWorld = mat3(pc.model);
        vec3 up = normalize(vUp);

        // The sea bed shows through the shallows, absorbed red first, under light scattered back by the water column.
        vec3 transmittance = exp(-depth * vec3(0.9, 0.16, 0.08));
        vec3 scattering = mix(body.shallowWater.rgb, body.deepWater.rgb, smoothstep(0.0, 250.0, depth));

        OceanSurface water = Ocean(vDetail, transpose(toWorld) * up, time, metresPerPixel);
        float foam = ShoreFoam(vDetail, depth, time);

        s.albedo = mix(s.albedo * transmittance + scattering * (1.0 - transmittance), vec3(0.8), foam);
        s.metallic = 0.0;
        s.roughness = mix(water.roughness, 0.8, foam);
        s.occlusion = 1.0;
        s.reflectance = 0.02;
        s.relief = 0.0;
        n = normalize(toWorld * water.normal);

    }

    // Lights come on as the sun sinks through civil twilight at this spot.
    if (body.shallowWater.w > 0.0 && vHeight > 0.0) {

        vec3 up = normalize(vUp);
        float night = 1.0 - smoothstep(-0.1, 0.03, dot(up, frame.sunDirection.xyz));

        if (night > 0.0) {

            s.emissive += CityLights(bodyUp, vDetail, vHeight, metresPerPixel, radiansPerPixel, body.settlements, body.shallowWater.w) * night;

        }

    }

    if (pc.detail.w > 0.0) {

        float level = pc.detail.w - 1.0;

        s.albedo = 0.5 + 0.5 * cos(6.2831 * (level / 7.0 + vec3(0.0, 0.33, 0.67)));

    }

    // Specular anti-aliasing: normals that vary within a pixel widen its highlight instead of sparkling.
    vec3 normalChange = fwidth(n);

    s.roughness = sqrt(clamp(s.roughness * s.roughness + min(dot(normalChange, normalChange) * 2.0, 0.4), 0.0, 1.0));

    s.occlusion *= ScreenOcclusion(gl_FragCoord.xy, s.albedo);
    s.albedo *= CascadeTint(vWorldPosition);

    outColor = SceneOutput(ShadeSurface(s, vWorldPosition, n, v, SunShadow(vWorldPosition, normalize(vNormal))), vWorldPosition, gl_FragCoord.xy);

}

// Detail materials for terrain. Mirrors TerrainBodyUniforms in Terrain/TerrainMaterials.cs.

struct TerrainBody {

    vec4 deepWater;
    vec4 shallowWater;
    ivec4 layers0;
    ivec4 layers1;
    vec4 tileScale0;
    vec4 tileScale1;
    vec4 settlements;
    vec4 streets;
    vec4 palette[8];
    vec4 inverseMean[8];

};

layout(set = 1, binding = 0) uniform TerrainBodies {

    TerrainBody bodies[4];

} terrain;

// rgb albedo (sRGB), a height.
layout(set = 1, binding = 1) uniform sampler2DArray albedoMaps;

// rg normal xy, b roughness, a occlusion.
layout(set = 1, binding = 2) uniform sampler2DArray surfaceMaps;

struct DetailSample {

    vec3 albedo;
    float height;
    vec3 normal;
    float roughness;
    float occlusion;

};

uint PatchHash(ivec2 cell, int layer) {

    uint h = uint(cell.x) * 1597334677u ^ uint(cell.y) * 3812015801u ^ uint(layer + 1) * 2798796415u;
    h = (h ^ (h >> 16u)) * 2246822519u;

    return h ^ (h >> 13u);

}

void SamplePlane(int layer, vec2 metres, vec2 gx, vec2 gy, float scale, mat2 rotation, out vec4 albedo, out vec4 surface) {

    // Power-of-two patch spacing closes exactly across the terrain's 65536 m coordinate wrap.
    float spacing = clamp(exp2(ceil(log2(1.0 / scale))), 1.0, 32768.0);
    vec2 lattice = metres / spacing;
    vec2 cell = floor(lattice);
    vec2 f = fract(lattice);
    vec2 middle = f.x > f.y ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
    vec2 corners[3] = vec2[](vec2(0.0), middle, vec2(1.0));
    vec3 weights = vec3(1.0 - max(f.x, f.y), abs(f.x - f.y), min(f.x, f.y));
    weights *= weights;
    weights /= dot(weights, vec3(1.0));
    albedo = vec4(0.0);
    surface = vec4(0.0);

    for (int i = 0; i < 3; i++) {

        vec2 corner = corners[i];
        uint h = PatchHash(ivec2(mod(cell + corner, 65536.0 / spacing)), layer);
        vec2 phase = vec2(h & 4095u, (h >> 12u) & 4095u) / 4096.0;
        vec2 signs = vec2((h & (1u << 24u)) == 0u ? 1.0 : -1.0, (h & (1u << 25u)) == 0u ? 1.0 : -1.0);
        mat2 basis = mat2(signs.x, 0.0, 0.0, signs.y) * rotation;

        if ((h & (1u << 26u)) != 0u) {

            basis = mat2(basis[0].yx, basis[1].yx);

        }

        vec3 uv = vec3(basis * ((f - corner) * (spacing * scale)) + phase, layer);
        vec2 dx = basis * (gx * scale);
        vec2 dy = basis * (gy * scale);
        vec4 sampleSurface = textureGrad(surfaceMaps, uv, dx, dy);
        sampleSurface.xy = transpose(basis) * (sampleSurface.xy - 0.5) + 0.5;
        albedo += weights[i] * textureGrad(albedoMaps, uv, dx, dy);
        surface += weights[i] * sampleSurface;

    }

}

// Triplanar in the body-fixed frame with UDN normals; near-zero planes are skipped.
DetailSample SampleDetail(int layer, vec3 position, vec3 dx, vec3 dy, vec3 blend, float scale, float angle) {

    mat2 r = mat2(cos(angle), sin(angle), -sin(angle), cos(angle));

    vec3 w = blend * step(vec3(0.02), blend);

    w /= w.x + w.y + w.z;

    vec4 albedo = vec4(0.0);
    vec4 surface = vec4(0.0);
    vec3 normal = vec3(0.0);

    vec4 a;
    vec4 s;

    if (w.x > 0.0) {

        SamplePlane(layer, position.zy, dx.zy, dy.zy, scale, r, a, s);

        albedo += a * w.x;
        surface += s * w.x;
        normal += vec3(0.0, s.y * 2.0 - 1.0, s.x * 2.0 - 1.0) * w.x;

    }

    if (w.y > 0.0) {

        SamplePlane(layer, position.xz, dx.xz, dy.xz, scale, r, a, s);

        albedo += a * w.y;
        surface += s * w.y;
        normal += vec3(s.x * 2.0 - 1.0, 0.0, s.y * 2.0 - 1.0) * w.y;

    }

    if (w.z > 0.0) {

        SamplePlane(layer, position.xy, dx.xy, dy.xy, scale, r, a, s);

        albedo += a * w.z;
        surface += s * w.z;
        normal += vec3(s.x * 2.0 - 1.0, s.y * 2.0 - 1.0, 0.0) * w.z;

    }

    DetailSample d;

    d.albedo = albedo.rgb;
    d.height = albedo.a;
    d.normal = normal;
    d.roughness = surface.b;
    d.occlusion = surface.a;

    return d;

}

DetailSample MixDetail(DetailSample a, DetailSample b, float t) {

    DetailSample s;

    s.albedo = mix(a.albedo, b.albedo, t);
    s.height = mix(a.height, b.height, t);
    s.normal = mix(a.normal, b.normal, t);
    s.roughness = mix(a.roughness, b.roughness, t);
    s.occlusion = mix(a.occlusion, b.occlusion, t);

    return s;

}

// Weight of a tiling level: 1 while a tile spans many pixels, 0 once it shrinks to a few.
float TileFade(float metresPerPixel, float scale) {

    return 1.0 - smoothstep(0.08, 0.3, metresPerPixel * scale);

}

// Two tiling levels, the coarser eight times larger, so detail carries further and repeats less. Fade is the coarse level's.
DetailSample SampleLayered(int layer, vec3 position, vec3 dx, vec3 dy, vec3 blend, float scale, float metresPerPixel, out float fade) {

    const float Coarse = 0.1173;

    float near = TileFade(metresPerPixel, scale);

    fade = TileFade(metresPerPixel, scale * Coarse);

    DetailSample coarse = SampleDetail(layer, position, dx, dy, blend, scale * Coarse, 0.61);

    if (near <= 0.0) {

        return coarse;

    }

    // Up close the coarse level still breaks up the repeats of the fine one.
    return MixDetail(coarse, SampleDetail(layer, position, dx, dy, blend, scale, 0.0), near * 0.75);

}

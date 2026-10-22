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

vec2 TileOffset(float index) {

    return fract(sin(vec2(index * 12.9898, index * 78.233)) * 43758.5453);

}

// One plane; a non-negative variant blends two noise-picked tile offsets so the grid never shows.
void SamplePlane(int layer, vec2 uv, vec2 gx, vec2 gy, float variant, out vec4 albedo, out vec4 surface) {

    if (variant < 0.0) {

        albedo = textureGrad(albedoMaps, vec3(uv, layer), gx, gy);
        surface = textureGrad(surfaceMaps, vec3(uv, layer), gx, gy);

        return;

    }

    float index = floor(variant);
    float t = smoothstep(0.2, 0.8, variant - index);

    vec2 a = uv + TileOffset(index);
    vec2 b = uv + TileOffset(index + 1.0);

    albedo = mix(textureGrad(albedoMaps, vec3(a, layer), gx, gy), textureGrad(albedoMaps, vec3(b, layer), gx, gy), t);
    surface = mix(textureGrad(surfaceMaps, vec3(a, layer), gx, gy), textureGrad(surfaceMaps, vec3(b, layer), gx, gy), t);

}

// Triplanar in the body-fixed frame with UDN normals; near-zero planes are skipped.
DetailSample SampleDetail(int layer, vec3 position, vec3 dx, vec3 dy, vec3 blend, float scale, float angle, float variant) {

    mat2 r = mat2(cos(angle), sin(angle), -sin(angle), cos(angle));

    vec3 p = position * scale;
    vec3 px = dx * scale;
    vec3 py = dy * scale;

    p.xy = r * p.xy;
    p.yz = r * p.yz;
    px.xy = r * px.xy;
    px.yz = r * px.yz;
    py.xy = r * py.xy;
    py.yz = r * py.yz;

    vec3 w = blend * step(vec3(0.02), blend);

    w /= w.x + w.y + w.z;

    vec4 albedo = vec4(0.0);
    vec4 surface = vec4(0.0);
    vec3 normal = vec3(0.0);

    vec4 a;
    vec4 s;

    if (w.x > 0.0) {

        SamplePlane(layer, p.zy, px.zy, py.zy, variant, a, s);

        albedo += a * w.x;
        surface += s * w.x;
        normal += vec3(0.0, s.y * 2.0 - 1.0, s.x * 2.0 - 1.0) * w.x;

    }

    if (w.y > 0.0) {

        SamplePlane(layer, p.xz, px.xz, py.xz, variant, a, s);

        albedo += a * w.y;
        surface += s * w.y;
        normal += vec3(s.x * 2.0 - 1.0, 0.0, s.y * 2.0 - 1.0) * w.y;

    }

    if (w.z > 0.0) {

        SamplePlane(layer, p.xy, px.xy, py.xy, variant, a, s);

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
DetailSample SampleLayered(int layer, vec3 position, vec3 dx, vec3 dy, vec3 blend, float scale, float metresPerPixel, float variant, out float fade) {

    const float Coarse = 0.1173;

    float near = TileFade(metresPerPixel, scale);

    fade = TileFade(metresPerPixel, scale * Coarse);

    DetailSample coarse = SampleDetail(layer, position, dx, dy, blend, scale * Coarse, 0.61, variant);

    if (near <= 0.0) {

        return coarse;

    }

    // Up close the coarse level still breaks up the repeats of the fine one.
    return MixDetail(coarse, SampleDetail(layer, position, dx, dy, blend, scale, 0.0, variant * 0.37), near * 0.75);

}

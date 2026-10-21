// Detail materials for terrain. Mirrors TerrainBodyUniforms in Terrain/TerrainMaterials.cs.

struct TerrainBody {

    vec4 deepWater;
    vec4 shallowWater;
    ivec4 layers0;
    ivec4 layers1;
    vec4 tileScale0;
    vec4 tileScale1;
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

// Triplanar projection in the body-fixed frame; normals use the UDN blend of each plane's tangent-space xy.
DetailSample SampleDetail(int layer, vec3 position, vec3 blend, float scale, float angle) {

    vec3 p = position * scale;
    mat2 r = mat2(cos(angle), sin(angle), -sin(angle), cos(angle));

    p.xy = r * p.xy;
    p.yz = r * p.yz;

    vec4 ax = texture(albedoMaps, vec3(p.zy, layer));
    vec4 ay = texture(albedoMaps, vec3(p.xz, layer));
    vec4 az = texture(albedoMaps, vec3(p.xy, layer));

    vec4 sx = texture(surfaceMaps, vec3(p.zy, layer));
    vec4 sy = texture(surfaceMaps, vec3(p.xz, layer));
    vec4 sz = texture(surfaceMaps, vec3(p.xy, layer));

    vec4 albedo = ax * blend.x + ay * blend.y + az * blend.z;
    vec4 surface = sx * blend.x + sy * blend.y + sz * blend.z;

    vec2 nx = sx.xy * 2.0 - 1.0;
    vec2 ny = sy.xy * 2.0 - 1.0;
    vec2 nz = sz.xy * 2.0 - 1.0;

    DetailSample s;

    s.albedo = albedo.rgb;
    s.height = albedo.a;
    s.normal = vec3(0.0, nx.y, nx.x) * blend.x + vec3(ny.x, 0.0, ny.y) * blend.y + vec3(nz.x, nz.y, 0.0) * blend.z;
    s.roughness = surface.b;
    s.occlusion = surface.a;

    return s;

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
DetailSample SampleLayered(int layer, vec3 position, vec3 blend, float scale, float metresPerPixel, out float fade) {

    const float Coarse = 0.1173;

    float near = TileFade(metresPerPixel, scale);

    fade = TileFade(metresPerPixel, scale * Coarse);

    DetailSample coarse = SampleDetail(layer, position, blend, scale * Coarse, 0.61);

    if (near <= 0.0) {

        return coarse;

    }

    // Up close the coarse level still breaks up the repeats of the fine one.
    return MixDetail(coarse, SampleDetail(layer, position, blend, scale, 0.0), near * 0.75);

}

#version 450

#include "common.glsl"

layout(location = 0) in vec3 vWorldPosition;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec4 vTangent;
layout(location = 3) in vec2 vUv;

// Mirrors MaterialUniforms in Rendering/ModelLibrary.cs.
layout(set = 1, binding = 0) uniform Material {

    vec4 baseColor;
    vec4 emissive;
    vec4 params;
    vec4 flags;

} material;

layout(set = 1, binding = 1) uniform sampler2D baseColorMap;
layout(set = 1, binding = 2) uniform sampler2D metallicRoughnessMap;
layout(set = 1, binding = 3) uniform sampler2D normalMap;
layout(set = 1, binding = 4) uniform sampler2D occlusionMap;
layout(set = 1, binding = 5) uniform sampler2D emissiveMap;

layout(location = 0) out vec4 outColor;

void main() {

    vec4 base = material.baseColor * texture(baseColorMap, vUv);

    if (material.flags.x > 0.5 && base.a < material.params.w) {

        discard;

    }

    vec4 mr = texture(metallicRoughnessMap, vUv);

    vec3 n = normalize(vNormal);

    if (!gl_FrontFacing) {

        n = -n;

    }

    // Tangent-space normal map; degenerate tangents fall back to the interpolated normal.
    vec3 t = vTangent.xyz - n * dot(n, vTangent.xyz);

    if (dot(t, t) > 1e-8) {

        t = normalize(t);

        vec3 b = cross(n, t) * (vTangent.w < 0.0 ? -1.0 : 1.0);
        vec3 tangentNormal = texture(normalMap, vUv).xyz * 2.0 - 1.0;

        tangentNormal.xy *= material.emissive.w;
        n = normalize(mat3(t, b, n) * tangentNormal);

    }

    Surface s;

    s.albedo = base.rgb;
    s.metallic = clamp(material.params.x * mr.b, 0.0, 1.0);
    s.roughness = clamp(material.params.y * mr.g, 0.0, 1.0);
    s.occlusion = 1.0 + material.params.z * (texture(occlusionMap, vUv).r - 1.0);
    s.emissive = material.emissive.rgb * texture(emissiveMap, vUv).rgb;
    s.reflectance = 0.04;

    vec3 v = normalize(frame.cameraPosition.xyz - vWorldPosition);

    vec3 geometric = gl_FrontFacing ? normalize(vNormal) : -normalize(vNormal);

    s.geometricNormal = geometric;
    s.relief = 0.0;

    s.occlusion *= ScreenOcclusion(gl_FragCoord.xy, s.albedo);
    s.albedo *= CascadeTint(vWorldPosition);

    outColor = SceneOutput(ShadeSurface(s, vWorldPosition, n, v, SunShadow(vWorldPosition, geometric)), vWorldPosition, gl_FragCoord.xy);

}

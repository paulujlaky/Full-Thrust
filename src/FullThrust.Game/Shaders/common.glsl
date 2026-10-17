// Shared by scene shaders. Mirrors FrameUniforms in Rendering/FrameResources.cs.

layout(set = 0, binding = 0) uniform Frame {

    mat4 view;
    mat4 projection;
    mat4 viewProjection;
    mat4 inverseViewProjection;

    vec4 cameraPosition;

    vec4 sunDirection;
    vec4 sunIlluminance;

    vec4 ambientUp;
    vec4 skyIrradiance;
    vec4 groundIrradiance;

    vec4 exposure;

} frame;

const float PI = 3.14159265359;

// Scene shaders write pre-exposed radiance so fp16 targets never see raw lux values.
vec3 Expose(vec3 radiance) {

    return min(radiance * frame.exposure.x, vec3(64000.0));

}

vec3 AmbientIrradiance(vec3 n) {

    float t = dot(n, frame.ambientUp.xyz) * 0.5 + 0.5;

    return mix(frame.groundIrradiance.rgb, frame.skyIrradiance.rgb, t);

}

float DistributionGgx(float nh, float roughness) {

    float a = roughness * roughness;
    float a2 = a * a;
    float d = nh * nh * (a2 - 1.0) + 1.0;

    return a2 / (PI * d * d);

}

float VisibilitySmithGgx(float nv, float nl, float roughness) {

    float a = roughness * roughness;
    float gv = nl * sqrt(nv * nv * (1.0 - a * a) + a * a);
    float gl = nv * sqrt(nl * nl * (1.0 - a * a) + a * a);

    return 0.5 / max(gv + gl, 1e-5);

}

vec3 FresnelSchlick(float vh, vec3 f0) {

    return f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);

}

vec3 FresnelSchlickRoughness(float nv, vec3 f0, float roughness) {

    return f0 + (max(vec3(1.0 - roughness), f0) - f0) * pow(1.0 - nv, 5.0);

}

struct Surface {

    vec3 albedo;
    float metallic;
    float roughness;
    float occlusion;
    vec3 emissive;

};

// Cook-Torrance GGX under the sun plus a hemisphere ambient; returns radiance before exposure.
vec3 ShadeSurface(Surface s, vec3 n, vec3 v, float shadow) {

    vec3 l = frame.sunDirection.xyz;
    vec3 h = normalize(l + v);

    float roughness = clamp(s.roughness, 0.045, 1.0);
    float nv = max(dot(n, v), 1e-4);
    float nl = max(dot(n, l), 0.0);
    float nh = max(dot(n, h), 0.0);
    float vh = max(dot(v, h), 0.0);

    vec3 f0 = mix(vec3(0.04), s.albedo, s.metallic);
    vec3 f = FresnelSchlick(vh, f0);

    vec3 specular = DistributionGgx(nh, roughness) * VisibilitySmithGgx(nv, nl, roughness) * f;
    vec3 diffuse = (1.0 - f) * (1.0 - s.metallic) * s.albedo / PI;

    vec3 direct = (diffuse + specular) * frame.sunIlluminance.rgb * nl * shadow;

    // Without an environment map, metals pick up the ambient through a roughness-weighted Fresnel term.
    vec3 ambientF = FresnelSchlickRoughness(nv, f0, roughness);
    vec3 irradiance = AmbientIrradiance(n);
    vec3 reflected = AmbientIrradiance(reflect(-v, n));
    vec3 ambient = ((1.0 - ambientF) * (1.0 - s.metallic) * s.albedo * irradiance + ambientF * mix(reflected, irradiance, roughness)) / PI;

    return direct + ambient * s.occlusion + s.emissive;

}

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

    mat4 shadowViewProjection[4];
    vec4 shadowSplits;
    vec4 shadowTexel;

    // x cascade count (0 disables), y normal offset in texels, z filter radius in texels, w tints cascades.
    vec4 shadowParams;

    // x: 1 when the ambient occlusion image holds this frame's result.
    vec4 aoParams;

    // Atmosphere of the nearest body with one. Centre xyz and bottom radius w.
    vec4 atmosphereCenter;

    // x top radius, y Mie anisotropy, z 1 when enabled, w ozone half width.
    vec4 atmosphereShape;

    // Coefficients per metre at the surface; w holds the scale height, or the ozone peak altitude.
    vec4 rayleighScattering;
    vec4 mieScattering;
    vec4 mieExtinction;
    vec4 ozoneAbsorption;
    vec4 groundAlbedo;

} frame;

layout(set = 0, binding = 1) uniform sampler2DArrayShadow shadowMap;
layout(set = 0, binding = 2) uniform sampler2D ambientOcclusion;
layout(set = 0, binding = 3) uniform sampler2D transmittanceLut;
layout(set = 0, binding = 4) uniform sampler2D multiScatterLut;
layout(set = 0, binding = 5) uniform sampler2D skyViewLut;
layout(set = 0, binding = 6) uniform sampler2D irradianceLut;

const float PI = 3.14159265359;

#include "atmosphere.glsl"

// World-space view ray through an NDC position, built in view space so it stays exact far from the floating origin.
vec3 ViewDirection(vec2 ndc) {

    vec3 view = vec3(ndc.x / frame.projection[0][0], ndc.y / frame.projection[1][1], -1.0);

    return normalize(transpose(mat3(frame.view)) * view);

}

// Scene shaders write pre-exposed radiance so fp16 targets never see raw lux values.
vec3 Expose(vec3 radiance) {

    return min(radiance * frame.exposure.x, vec3(64000.0));

}

// Near a body with an atmosphere its sky and ground light; elsewhere the camera's hemisphere of planetshine.
vec3 AmbientIrradiance(vec3 p, vec3 n) {

    float t = dot(n, frame.ambientUp.xyz) * 0.5 + 0.5;
    vec3 hemisphere = mix(frame.groundIrradiance.rgb, frame.skyIrradiance.rgb, t);
    float weight = AtmosphereWeight(p);

    if (weight <= 0.0) {

        return hemisphere;

    }

    // ambientUp.w is the night-time floor and groundIrradiance.w the overlay's ambient scale.
    vec3 atmosphere = AtmosphereIrradiance(p, n) * frame.groundIrradiance.w + frame.ambientUp.w;

    return mix(hemisphere, atmosphere, weight);

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

    // Normal-incidence reflectance of a dielectric: 0.04 for most, 0.02 for water.
    float reflectance;

};

// Cook-Torrance GGX under the sun plus a hemisphere ambient; returns radiance before exposure.
vec3 ShadeSurface(Surface s, vec3 p, vec3 n, vec3 v, float shadow) {

    vec3 l = frame.sunDirection.xyz;
    vec3 h = normalize(l + v);

    float roughness = clamp(s.roughness, 0.045, 1.0);
    float nv = max(dot(n, v), 1e-4);
    float nl = max(dot(n, l), 0.0);
    float nh = max(dot(n, h), 0.0);
    float vh = max(dot(v, h), 0.0);

    vec3 f0 = mix(vec3(s.reflectance), s.albedo, s.metallic);
    vec3 f = FresnelSchlick(vh, f0);

    vec3 specular = DistributionGgx(nh, roughness) * VisibilitySmithGgx(nv, nl, roughness) * f;
    vec3 diffuse = (1.0 - f) * (1.0 - s.metallic) * s.albedo / PI;

    vec3 direct = (diffuse + specular) * SunIlluminanceAt(p) * nl * shadow;

    // Without an environment map, metals pick up the ambient through a roughness-weighted Fresnel term.
    vec3 ambientF = FresnelSchlickRoughness(nv, f0, roughness);
    vec3 irradiance = AmbientIrradiance(p, n);
    vec3 reflected = AmbientIrradiance(p, reflect(-v, n));
    vec3 ambient = ((1.0 - ambientF) * (1.0 - s.metallic) * s.albedo * irradiance + ambientF * mix(reflected, irradiance, roughness)) / PI;

    return direct + ambient * s.occlusion + s.emissive;

}

float ViewDepth(vec3 worldPosition) {

    return -(frame.view * vec4(worldPosition, 1.0)).z;

}

int ShadowCascade(float viewDepth) {

    int count = int(frame.shadowParams.x);

    for (int i = 0; i < count; i++) {

        if (viewDepth < frame.shadowSplits[i]) {

            return i;

        }

    }

    return count;

}

// 4x4 bilinear comparison taps: a smooth 5x5-texel tent around the receiver, scaled by the filter radius.
float SampleCascade(int cascade, vec3 worldPosition, vec3 normal) {

    vec3 offset = normal * frame.shadowTexel[cascade] * frame.shadowParams.y;
    vec4 clip = frame.shadowViewProjection[cascade] * vec4(worldPosition + offset, 1.0);
    vec2 uv = clip.xy * 0.5 + 0.5;

    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0))) || clip.z > 1.0) {

        return 1.0;

    }

    vec2 texel = frame.shadowParams.z / vec2(textureSize(shadowMap, 0).xy);
    float sum = 0.0;

    for (int y = 0; y < 4; y++) {

        for (int x = 0; x < 4; x++) {

            vec2 tap = uv + (vec2(x, y) - 1.5) * texel;

            sum += texture(shadowMap, vec4(tap, float(cascade), clip.z));

        }

    }

    return sum / 16.0;

}

// Sun visibility from the cascades; the last tenth of each cascade blends into the next so seams do not show.
float SunShadow(vec3 worldPosition, vec3 normal) {

    int count = int(frame.shadowParams.x);

    if (count == 0) {

        return 1.0;

    }

    float depth = ViewDepth(worldPosition);
    int cascade = ShadowCascade(depth);

    if (cascade >= count) {

        return 1.0;

    }

    float shadow = SampleCascade(cascade, worldPosition, normal);

    float end = frame.shadowSplits[cascade];
    float start = cascade == 0 ? 0.0 : frame.shadowSplits[cascade - 1];
    float blend = smoothstep(end - (end - start) * 0.1, end, depth);

    if (blend > 0.0) {

        float next = cascade + 1 < count ? SampleCascade(cascade + 1, worldPosition, normal) : 1.0;

        shadow = mix(shadow, next, blend);

    }

    return shadow;

}

vec3 CascadeTint(vec3 worldPosition) {

    const vec3 tints[5] = vec3[](vec3(1.0, 0.35, 0.35), vec3(0.35, 1.0, 0.35), vec3(0.35, 0.5, 1.0), vec3(1.0, 1.0, 0.35), vec3(1.0));

    return frame.shadowParams.w > 0.5 ? tints[min(ShadowCascade(ViewDepth(worldPosition)), 4)] : vec3(1.0);

}

// Screen-space occlusion with Jimenez's multi-bounce fit, which brightens it on light surfaces.
float ScreenOcclusion(vec2 fragCoord, vec3 albedo) {

    if (frame.aoParams.x < 0.5) {

        return 1.0;

    }

    float visibility = texelFetch(ambientOcclusion, ivec2(fragCoord), 0).r;
    float luminance = dot(albedo, vec3(0.2126, 0.7152, 0.0722));

    float a = 2.0404 * luminance - 0.3324;
    float b = -4.7951 * luminance + 0.6417;
    float c = 2.7552 * luminance + 0.6903;

    return max(visibility, ((visibility * a + b) * visibility + c) * visibility);

}

// The overlay can show the raw occlusion in place of the shaded scene.
vec4 SceneOutput(vec3 radiance, vec2 fragCoord) {

    if (frame.aoParams.y > 0.5) {

        return vec4(texelFetch(ambientOcclusion, ivec2(fragCoord), 0).rrr, 1.0);

    }

    return vec4(Expose(radiance), 1.0);

}

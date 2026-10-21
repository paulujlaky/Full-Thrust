#version 450

#include "common.glsl"

layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec3 vObjectNormal;
layout(location = 2) in vec3 vWorldPosition;

layout(push_constant) uniform Push {

    mat4 model;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 color;

} pc;

layout(location = 0) out vec4 outColor;

void main() {

    vec3 n = normalize(vNormal);
    vec3 v = normalize(frame.cameraPosition.xyz - vWorldPosition);

    // Alpha below 1 encodes checker strength in body space, so rotation is visible on plain spheres.
    vec3 o = normalize(vObjectNormal);
    float longitude = atan(o.z, o.x) / (2.0 * PI) + 0.5;
    float latitude = asin(clamp(o.y, -1.0, 1.0)) / PI + 0.5;
    float checker = mod(floor(longitude * 24.0) + floor(latitude * 12.0), 2.0);
    float tint = 1.0 - (1.0 - pc.color.a) * checker;

    Surface s;

    s.albedo = pc.color.rgb * tint;
    s.metallic = 0.0;
    s.roughness = 0.7;
    s.occlusion = 1.0;
    s.emissive = vec3(0.0);
    s.reflectance = 0.04;

    outColor = vec4(Expose(ShadeSurface(s, n, v, 1.0)), 1.0);

}

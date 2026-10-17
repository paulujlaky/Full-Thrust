#version 450

#include "common.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUv;
layout(location = 3) in vec4 inTangent;

layout(push_constant) uniform Push {

    mat4 model;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;

} pc;

layout(location = 0) out vec3 vWorldPosition;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec4 vTangent;
layout(location = 3) out vec2 vUv;

void main() {

    vec4 world = pc.model * vec4(inPosition, 1.0);
    mat3 normalMatrix = mat3(pc.normal0.xyz, pc.normal1.xyz, pc.normal2.xyz);

    gl_Position = frame.viewProjection * world;

    vWorldPosition = world.xyz;
    vNormal = normalMatrix * inNormal;
    vTangent = vec4(mat3(pc.model) * inTangent.xyz, inTangent.w);
    vUv = inUv;

}

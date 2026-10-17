#version 450

#include "common.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

layout(push_constant) uniform Push {

    mat4 model;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 color;

} pc;

layout(location = 0) out vec3 vNormal;
layout(location = 1) out vec3 vObjectNormal;
layout(location = 2) out vec3 vWorldPosition;

void main() {

    vec4 world = pc.model * vec4(inPosition, 1.0);

    gl_Position = frame.viewProjection * world;

    vWorldPosition = world.xyz;
    vNormal = mat3(pc.normal0.xyz, pc.normal1.xyz, pc.normal2.xyz) * inNormal;
    vObjectNormal = inNormal;

}

#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

layout(push_constant) uniform Push {

    mat4 mvp;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 color;

} pc;

layout(location = 0) out vec3 vNormal;
layout(location = 1) out vec3 vObjectNormal;

void main() {

    gl_Position = pc.mvp * vec4(inPosition, 1.0);
    vNormal = mat3(pc.normal0.xyz, pc.normal1.xyz, pc.normal2.xyz) * inNormal;
    vObjectNormal = inNormal;

}

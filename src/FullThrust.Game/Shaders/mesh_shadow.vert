#version 450

#include "common.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 2) in vec2 inUv;
layout(location = 3) out vec2 vUv;

// Mirrors MeshPush in Rendering/ModelLibrary.cs.
layout(push_constant) uniform Push {

    mat4 model;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    ivec4 view;

} pc;

void main() {

    gl_Position = frame.shadowViewProjection[pc.view.x] * (pc.model * vec4(inPosition, 1.0));
    vUv = inUv;

}

#version 450

#include "common.glsl"
#include "terrain_push.glsl"

layout(location = 0) in vec3 inPosition;

void main() {

    gl_Position = frame.shadowViewProjection[int(pc.center.w)] * (pc.model * vec4(inPosition, 1.0));

}

#version 450

#include "common.glsl"
#include "terrain_push.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in uint inColor;
layout(location = 3) in float inHeight;

layout(location = 0) out vec3 vWorldPosition;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec4 vSurface;
layout(location = 3) out vec3 vDetail;
layout(location = 4) out float vHeight;
layout(location = 5) out vec3 vUp;

void main() {

    vec4 world = pc.model * vec4(inPosition, 1.0);

    gl_Position = frame.viewProjection * world;

    vWorldPosition = world.xyz;
    vNormal = mat3(pc.model) * inNormal;
    vSurface = unpackUnorm4x8(inColor);
    vDetail = pc.detail.xyz + inPosition;
    vHeight = inHeight;

    // Radial direction from the body centre: water is flat however the ground under it slopes.
    vUp = mat3(pc.model) * normalize(pc.center.xyz + inPosition);

}

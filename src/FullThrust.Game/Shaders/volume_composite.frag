#version 450

#include "common.glsl"

layout(location = 0) in vec2 vUv;

layout(set = 1, binding = 0) uniform sampler2D volumeColor;
layout(set = 1, binding = 1) uniform sampler2D volumeDepth;
layout(set = 1, binding = 2) uniform sampler2D sceneDepth;

layout(location = 0) out vec4 outColor;

float ViewDistance(float depth) {

    return frame.projection[3][2] / max(depth, 1e-12);

}

// Bilateral upsample of the half-resolution volumes; blending gives scene * transmittance + light.
void main() {

    ivec2 size = textureSize(volumeColor, 0);
    vec2 position = gl_FragCoord.xy * 0.5 - 0.5;
    ivec2 base = ivec2(floor(position));
    vec2 f = position - vec2(base);

    float distance = ViewDistance(texelFetch(sceneDepth, ivec2(gl_FragCoord.xy), 0).r);

    vec4 sum = vec4(0.0);
    float weights = 0.0;

    for (int y = 0; y < 2; y++) {

        for (int x = 0; x < 2; x++) {

            ivec2 tap = clamp(base + ivec2(x, y), ivec2(0), size - 1);
            float bilinear = (x == 0 ? 1.0 - f.x : f.x) * (y == 0 ? 1.0 - f.y : f.y);
            float difference = abs(ViewDistance(texelFetch(volumeDepth, tap, 0).r) - distance) / (distance * 0.02 + 0.05);
            float weight = bilinear * exp(-difference * difference) + 1e-5 * bilinear;

            sum += texelFetch(volumeColor, tap, 0) * weight;
            weights += weight;

        }

    }

    outColor = sum / weights;

}

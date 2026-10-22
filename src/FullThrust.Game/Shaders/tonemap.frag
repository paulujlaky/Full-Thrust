#version 450

layout(location = 0) in vec2 vUv;

layout(set = 0, binding = 0) uniform sampler2D hdr;
layout(set = 0, binding = 1) uniform sampler2D bloom;

layout(push_constant) uniform Push {

    int tonemapper;
    float whitePoint;
    float bloomIntensity;

} pc;

layout(location = 0) out vec4 outColor;

// Narkowicz's fit of the ACES RRT and sRGB ODT.
vec3 Aces(vec3 x) {

    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);

}

// Per channel, a bright orange glow saturates every channel and turns white; past white this leans towards scaling by the brightest channel, which keeps the hue.
vec3 HueKeepingAces(vec3 color) {

    float peak = max(color.r, max(color.g, color.b));
    vec3 channels = Aces(color);

    if (peak <= 1.0) {

        return channels;

    }

    vec3 hue = color * (Aces(vec3(peak)).r / peak);

    return mix(channels, hue, 0.7 * smoothstep(1.0, 4.0, peak));

}

// Minimal AgX (Troy Sobotka) with a sigmoid fit; output is linear sRGB.
vec3 AgX(vec3 color) {

    const mat3 inset = mat3(0.842479, 0.0423282, 0.0423756, 0.0784336, 0.878468, 0.0784336, 0.0792237, 0.0791661, 0.879142);
    const mat3 outset = mat3(1.19688, -0.0528969, -0.0529716, -0.0980209, 1.15190, -0.0980435, -0.0990297, -0.0989612, 1.15107);
    const float minEv = -12.47393;
    const float maxEv = 4.026069;

    vec3 x = inset * max(color, vec3(1e-10));

    x = clamp((log2(x) - minEv) / (maxEv - minEv), 0.0, 1.0);

    vec3 x2 = x * x;
    vec3 x4 = x2 * x2;

    x = 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
    x = outset * x;

    return pow(max(x, vec3(0.0)), vec3(2.2));

}

// Khronos PBR Neutral: keeps base colours faithful up to the highlights.
vec3 PbrNeutral(vec3 color) {

    const float startCompression = 0.8 - 0.04;
    const float desaturation = 0.15;

    float x = min(color.r, min(color.g, color.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;

    color -= offset;

    float peak = max(color.r, max(color.g, color.b));

    if (peak < startCompression) {

        return color;

    }

    float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);

    color *= newPeak / peak;

    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);

    return mix(color, vec3(newPeak), g);

}

vec3 Reinhard(vec3 x, float white) {

    return x * (1.0 + x / (white * white)) / (1.0 + x);

}

// Interleaved gradient noise breaks up banding in dark gradients before 8-bit quantisation.
float Dither(vec2 pixel) {

    return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715)))) - 0.5;

}

void main() {

    // Energy-conserving: bloom takes its share of the image rather than adding light.
    vec3 color = mix(texture(hdr, vUv).rgb, texture(bloom, vUv).rgb, pc.bloomIntensity);

    vec3 mapped;

    switch (pc.tonemapper) {

        case 0: mapped = HueKeepingAces(color); break;
        case 1: mapped = AgX(color); break;
        case 2: mapped = PbrNeutral(color); break;
        case 3: mapped = Reinhard(color, pc.whitePoint); break;
        default: mapped = clamp(color, 0.0, 1.0); break;

    }

    // The swapchain encodes sRGB, so dither in (approximately) encoded space and convert back.
    vec3 encoded = pow(clamp(mapped, 0.0, 1.0), vec3(1.0 / 2.2)) + Dither(gl_FragCoord.xy) / 255.0;

    outColor = vec4(pow(max(encoded, vec3(0.0)), vec3(2.2)), 1.0);

}

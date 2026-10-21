// Shared by the ambient occlusion kernels. Mirrors AoPush in Rendering/AmbientOcclusion.cs.

layout(local_size_x = 8, local_size_y = 8) in;

layout(set = 1, binding = 0) uniform sampler2D sceneDepth;
layout(set = 1, binding = 1, r8) uniform image2D rawOcclusion;
layout(set = 1, binding = 2, r8) uniform writeonly image2D filteredOcclusion;

layout(push_constant) uniform Push {

    // x, y: projection scale on each axis; z: near plane.
    vec4 projection;

    // x radius in metres, y falloff fraction of the radius, z power, w largest radius in pixels.
    vec4 params;

    ivec4 size;

} pc;

// Infinite reverse-Z: depth is near / distance.
float ViewDistance(float depth) {

    return pc.projection.z / max(depth, 1e-12);

}

vec3 ViewPosition(ivec2 pixel) {

    float depth = texelFetch(sceneDepth, pixel, 0).r;
    float distance = ViewDistance(depth);
    vec2 ndc = (vec2(pixel) + 0.5) / vec2(pc.size.xy) * 2.0 - 1.0;

    return vec3(ndc.x * distance / pc.projection.x, -ndc.y * distance / pc.projection.y, -distance);

}

// A 4x4 ordered pattern, so the 4x4 filter that follows cancels it exactly.
float OrderedNoise(ivec2 pixel) {

    const float pattern[16] = float[](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);

    return (pattern[(pixel.x & 3) + (pixel.y & 3) * 4] + 0.5) / 16.0;

}

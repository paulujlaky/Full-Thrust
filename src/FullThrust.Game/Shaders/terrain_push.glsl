// Mirrors TerrainPush in Terrain/TerrainRenderer.cs.
layout(push_constant) uniform Push {

    mat4 model;
    vec4 detail;
    vec4 deepWater;
    vec4 shallowWater;

    // Chunk centre in the body-fixed frame, so water can use the true radial direction.
    vec4 center;

} pc;

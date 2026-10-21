// Mirrors TerrainPush in Terrain/TerrainRenderer.cs.
layout(push_constant) uniform Push {

    mat4 model;
    vec4 detail;

    // xyz: chunk centre in the body-fixed frame; w: shadow cascade for the shadow vertex shader.
    vec4 center;

    // x: the body's slot in the terrain material uniforms.
    ivec4 body;

} pc;

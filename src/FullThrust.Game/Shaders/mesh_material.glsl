// Mirrors MaterialUniforms in the model library.
layout(set = 1, binding = 0) uniform Material {

    vec4 baseColor;
    vec4 emissive;
    vec4 params;
    vec4 flags;

} material;

layout(set = 1, binding = 1) uniform sampler2D baseColorMap;

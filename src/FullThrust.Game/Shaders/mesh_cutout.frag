#version 450

#include "mesh_material.glsl"

layout(location = 3) in vec2 vUv;

void main() {

    if ((!gl_FrontFacing && material.flags.y < 0.5) || material.baseColor.a * texture(baseColorMap, vUv).a < material.params.w) {

        discard;

    }

}

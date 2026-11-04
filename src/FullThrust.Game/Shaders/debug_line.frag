#version 450

layout(location = 0) in vec4 vColor;

layout(location = 1) noperspective in float edge;

layout(location = 0) out vec4 outColor;

void main() {

    outColor = vec4(vColor.rgb, vColor.a * (1.0 - smoothstep(0.5, 1.5, abs(edge))));

}

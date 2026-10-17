#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec4 inColor;

layout(push_constant) uniform Push {

    mat4 viewProjection;

} pc;

layout(location = 0) out vec4 vColor;

void main() {

    gl_Position = pc.viewProjection * vec4(inPosition, 1.0);
    vColor = inColor;

}

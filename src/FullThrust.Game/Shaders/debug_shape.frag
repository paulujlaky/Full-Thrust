#version 450

layout(location = 0) in vec3 vNormal;

layout(push_constant) uniform Push {

    mat4 mvp;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 color;

} pc;

layout(location = 0) out vec4 outColor;

void main() {

    // Sun direction lives in w components to stay within the 128-byte push constant budget.
    vec3 sun = normalize(vec3(pc.normal0.w, pc.normal1.w, pc.normal2.w));
    vec3 n = normalize(vNormal);

    float diffuse = max(dot(n, sun), 0.0);
    float ambient = 0.08 + 0.04 * (n.y * 0.5 + 0.5);

    outColor = vec4(pc.color.rgb * (diffuse + ambient), pc.color.a);

}

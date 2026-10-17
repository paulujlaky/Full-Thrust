#version 450

layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec3 vObjectNormal;

layout(push_constant) uniform Push {

    mat4 mvp;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 color;

} pc;

layout(location = 0) out vec4 outColor;

const float PI = 3.14159265;

void main() {

    // Sun direction lives in w components to stay within the 128-byte push constant budget.
    vec3 sun = normalize(vec3(pc.normal0.w, pc.normal1.w, pc.normal2.w));
    vec3 n = normalize(vNormal);

    float diffuse = max(dot(n, sun), 0.0);
    float ambient = 0.08 + 0.04 * (n.y * 0.5 + 0.5);

    // Alpha below 1 encodes checker strength in body space, so rotation is visible on plain spheres.
    vec3 o = normalize(vObjectNormal);
    float longitude = atan(o.z, o.x) / (2.0 * PI) + 0.5;
    float latitude = asin(clamp(o.y, -1.0, 1.0)) / PI + 0.5;
    float checker = mod(floor(longitude * 24.0) + floor(latitude * 12.0), 2.0);
    float tint = 1.0 - (1.0 - pc.color.a) * checker;

    outColor = vec4(pc.color.rgb * tint * (diffuse + ambient), 1.0);

}

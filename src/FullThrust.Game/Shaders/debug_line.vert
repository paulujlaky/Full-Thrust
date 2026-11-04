#version 450

layout(location = 0) in vec3 startPosition;
layout(location = 1) in vec4 startColor;
layout(location = 2) in vec3 endPosition;
layout(location = 3) in vec4 endColor;

layout(push_constant) uniform Push {

    mat4 viewProjection;
    vec2 viewport;

} pc;

layout(location = 0) out vec4 vColor;
layout(location = 1) noperspective out float edge;

void main() {

    vec4 a = pc.viewProjection * vec4(startPosition, 1.0);
    vec4 b = pc.viewProjection * vec4(endPosition, 1.0);
    float da = a.w - a.z;
    float db = b.w - b.z;
    vColor = startColor;
    edge = 0.0;

    if (max(da, db) < 0.0) {

        gl_Position = vec4(2.0, 2.0, 0.0, 1.0);
        return;

    }

    if (min(da, db) < 0.0) {

        vec4 clipped = mix(a, b, da / (da - db));
        if (da < 0.0) {

            a = clipped;

        } else {

            b = clipped;

        }

    }

    vec2 tangent = (b.xy / b.w - a.xy / a.w) * pc.viewport;
    vec2 normal = vec2(-tangent.y, tangent.x) / max(length(tangent), 0.001);
    const int corners[6] = int[](0, 1, 2, 2, 1, 3);
    int corner = corners[gl_VertexIndex];
    edge = (corner % 2 == 0 ? -1.5 : 1.5);
    gl_Position = corner < 2 ? a : b;
    vColor = corner < 2 ? startColor : endColor;
    gl_Position.xy += (2.0 * edge / pc.viewport) * normal * gl_Position.w;

}

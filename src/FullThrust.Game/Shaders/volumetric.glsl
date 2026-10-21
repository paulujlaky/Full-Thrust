// Participating media shared by every volume: primitives, density by kind, and lighting. Positions are render-local metres.

const int VolumeNoiseSphere = 1;
const int VolumePlume = 2;
const int VolumeCloudLayer = 3;

// Mirrors VolumePrimitive in Rendering/Volumetrics/VolumePrimitive.cs.
struct VolumePrimitive {

    ivec4 kind;
    vec4 position;
    vec4 axis;
    vec4 medium;
    vec4 emission;
    vec4 params0;
    vec4 params1;

};

layout(set = 1, binding = 0) uniform sampler2D sceneDepth;
layout(set = 1, binding = 1) uniform sampler3D shapeNoise;
layout(set = 1, binding = 2) uniform sampler3D detailNoise;

layout(std430, set = 1, binding = 3) readonly buffer Volumes {

    ivec4 count;
    VolumePrimitive items[];

} volumes;

struct VolumeSample {

    // Per metre.
    float extinction;
    vec3 emission;

};

// Bounding segment of a primitive along a ray; false when the ray misses it.
bool VolumeSegment(VolumePrimitive v, vec3 origin, vec3 direction, out float enter, out float exit) {

    float near;
    float far;

    if (!RaySphere(origin - v.position.xyz, direction, v.position.w, near, far) || far <= 0.0) {

        enter = 0.0;
        exit = 0.0;

        return false;

    }

    enter = max(near, 0.0);
    exit = far;

    return true;

}

// A noisy ball of smoke: params0.x noise scale in metres.
VolumeSample NoiseSphere(VolumePrimitive v, vec3 p) {

    vec3 local = p - v.position.xyz;
    float radial = length(local) / v.position.w;

    vec4 shape = texture(shapeNoise, local / v.params0.x + v.params0.yzw);
    float erosion = texture(detailNoise, local / (v.params0.x * 0.25)).r;

    float base = (1.0 - smoothstep(0.4, 1.0, radial)) * shape.r;
    float density = clamp(Remap(base, 0.25 + erosion * 0.2, 1.0, 0.0, 1.0), 0.0, 1.0);

    VolumeSample s;

    s.extinction = density * v.medium.w;
    s.emission = v.emission.rgb * density;

    return s;

}

VolumeSample SampleVolume(VolumePrimitive v, vec3 p) {

    if (v.kind.x == VolumeNoiseSphere) {

        return NoiseSphere(v, p);

    }

    VolumeSample empty;

    empty.extinction = 0.0;
    empty.emission = vec3(0.0);

    return empty;

}

float HenyeyGreenstein(float cosTheta, float g) {

    float g2 = g * g;

    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));

}

// Optical depth towards the sun through the same primitive, for self-shadowing.
float SunOpticalDepth(VolumePrimitive v, vec3 p, vec3 sunDirection) {

    int steps = v.kind.z;
    float dt = v.position.w * 2.0 / float(max(steps, 1));
    float depth = 0.0;

    for (int i = 0; i < steps; i++) {

        depth += SampleVolume(v, p + sunDirection * (float(i) + 0.5) * dt).extinction * dt;

    }

    return depth;

}

struct VolumeResult {

    vec3 luminance;
    float transmittance;

    // Transmittance-weighted mean depth of what was seen, for compositing.
    float depth;

};

// Marches one primitive between enter and exit, adding to the running result. Luminance is before exposure.
void MarchVolume(VolumePrimitive v, vec3 origin, vec3 direction, float enter, float exit, float jitter, inout VolumeResult result) {

    int steps = max(v.kind.y, 1);
    float dt = (exit - enter) / float(steps);
    vec3 sunDirection = frame.sunDirection.xyz;
    float cosTheta = dot(direction, sunDirection);
    float g = v.emission.w;
    vec3 albedo = v.medium.rgb;

    for (int i = 0; i < steps; i++) {

        float t = enter + (float(i) + jitter) * dt;
        vec3 p = origin + direction * t;
        VolumeSample s = SampleVolume(v, p);

        if (s.extinction <= 1e-6 && dot(s.emission, s.emission) <= 0.0) {

            continue;

        }

        vec3 sun = SunIlluminanceAt(p);
        float shadow = SunOpticalDepth(v, p, sunDirection);
        vec3 up = normalize(PlanetRelative(p));
        vec3 ambient = (AmbientIrradiance(p, up) + AmbientIrradiance(p, -up)) / (4.0 * PI);

        // Multiple scattering as fading octaves of the single-scattering term (Wrenninge 2013).
        vec3 scattered = vec3(0.0);
        float a = 1.0;
        float b = 1.0;
        float c = 1.0;

        for (int octave = 0; octave < 3; octave++) {

            scattered += a * sun * exp(-shadow * b) * mix(HenyeyGreenstein(cosTheta, g * c), HenyeyGreenstein(cosTheta, -0.3 * c), 0.25);
            a *= 0.5;
            b *= 0.4;
            c *= 0.5;

        }

        vec3 source = albedo * s.extinction * (scattered + ambient) + s.emission;
        float stepTransmittance = exp(-s.extinction * dt);
        float extinction = max(s.extinction, 1e-6);

        result.luminance += result.transmittance * source * (1.0 - stepTransmittance) / extinction;
        result.depth += result.transmittance * (1.0 - stepTransmittance) * t;
        result.transmittance *= stepTransmittance;

        if (result.transmittance < 0.005) {

            break;

        }

    }

}

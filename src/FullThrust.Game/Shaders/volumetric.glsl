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
    vec4 params2;

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

// Metres covered by one volume pixel at the current sample, set while marching; noise finer than this is faded out.
float volumeFootprint = 0.0;

// The first stretch of a spherical shell along a ray, from below, inside or above it; long grazing paths are capped.
bool ShellSegment(VolumePrimitive v, vec3 origin, vec3 direction, out float enter, out float exit) {

    const float MaxSpan = 80000.0;

    vec3 o = origin - v.params0.xyz;
    float r = length(o);
    float inner = v.params0.w;
    float outer = v.params1.x;

    float outerNear;
    float outerFar;
    float innerNear;
    float innerFar;

    enter = 0.0;
    exit = 0.0;

    if (!RaySphere(o, direction, outer, outerNear, outerFar) || outerFar <= 0.0) {

        return false;

    }

    bool hitsInner = RaySphere(o, direction, inner, innerNear, innerFar);

    if (r < inner) {

        enter = innerFar;
        exit = outerFar;

    } else {

        enter = r < outer ? 0.0 : outerNear;
        exit = hitsInner && innerNear > 0.0 ? innerNear : outerFar;

    }

    exit = min(exit, enter + MaxSpan);

    return exit > enter;

}

// Bounding segment of a primitive along a ray; false when the ray misses it.
bool VolumeSegment(VolumePrimitive v, vec3 origin, vec3 direction, out float enter, out float exit) {

    float near;
    float far;

    if (v.kind.x == VolumeCloudLayer) {

        return ShellSegment(v, origin, direction, enter, exit);

    }

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

// Rocket exhaust: params0 nozzle exit and radius, axis direction and length, params1 spread, diamond spacing and strength.
VolumeSample Plume(VolumePrimitive v, vec3 p) {

    VolumeSample s;

    s.extinction = 0.0;
    s.emission = vec3(0.0);

    vec3 offset = p - v.params0.xyz;
    float exitRadius = v.params0.w;
    float x = dot(offset, v.axis.xyz);
    float span = v.axis.w;

    if (x < 0.0 || x > span) {

        return s;

    }

    float r = distance(offset, v.axis.xyz * x);
    float radius = exitRadius + x * (v.params1.x + 0.02);
    float dilution = exitRadius * exitRadius / (radius * radius);

    float profile = exp(-2.5 * (r * r) / (radius * radius));
    float along = (1.0 - x / span) * (1.0 - x / span) * smoothstep(0.0, exitRadius * 0.3, x);

    // Turbulence streams downstream with the flow and grows with distance from the nozzle.
    vec3 flow = offset / (exitRadius * 4.0) - v.axis.xyz * (frame.exposure.w * 6.0);
    float turbulence = mix(1.0, texture(shapeNoise, flow).g * 1.8, smoothstep(0.0, span * 0.3, x));

    float density = profile * dilution * along * turbulence;

    float spacing = max(v.params1.y, 1e-3);
    float wave = 0.5 + 0.5 * cos(6.2831853 * x / spacing);
    float diamonds = v.params1.z * pow(wave, 6.0) * exp(-(r * r) / (0.36 * radius * radius)) * exp(-x / (spacing * 5.0));

    vec3 hot = v.emission.rgb * (0.3 + 0.7 * exp(-x / (span * 0.25)));
    vec3 white = vec3(dot(v.emission.rgb, vec3(0.2126, 0.7152, 0.0722)));

    s.extinction = density * v.medium.w;
    // Afterburning keeps the exhaust glowing as it spreads, so emission thins more slowly than mass.
    s.emission = hot * profile * sqrt(dilution) * along * turbulence + mix(v.emission.rgb, white, 0.6) * diamonds * 2.0;

    return s;

}

// Cumulus after Schneider's Nubis: a body-fixed weather map sets coverage and height, Perlin-Worley billows, Worley wisps.
VolumeSample CloudLayer(VolumePrimitive v, vec3 p) {

    VolumeSample s;

    s.extinction = 0.0;
    s.emission = vec3(0.0);

    vec3 relative = p - v.params0.xyz;
    float r = length(relative);
    float h = (r - v.params0.w) / (v.params1.x - v.params0.w);

    if (h <= 0.0 || h >= 1.0) {

        return s;

    }

    vec3 body = QuaternionRotate(v.axis, relative);
    float weather = CloudWeather(shapeNoise, body, v.params1.z, v.params1.w, v.params1.y);

    if (weather <= 0.0) {

        return s;

    }

    // Flat bases and rounded tops that rise where the weather is densest.
    float top = 0.35 + 0.65 * weather;
    float gradient = clamp(Remap(h, 0.0, 0.08, 0.0, 1.0), 0.0, 1.0) * clamp(Remap(h, top * 0.6, top, 1.0, 0.0), 0.0, 1.0);

    if (gradient <= 0.0) {

        return s;

    }

    // Far away the billows fade to a value that keeps about the same area covered.
    float shapeWeight = 1.0 - smoothstep(0.1, 0.4, volumeFootprint * v.params2.z);
    float detailWeight = 1.0 - smoothstep(0.1, 0.4, volumeFootprint * v.params2.w);
    float billows = 0.85;

    if (shapeWeight < 1.0) {

        billows = 0.6 + 0.4 * texture(shapeNoise, body * (v.params2.z * 0.125) + 0.37).r;

    }

    if (shapeWeight > 0.0) {

        vec4 shape = texture(shapeNoise, body * v.params2.z + vec3(v.params2.x, 0.0, 0.0));
        float fbm = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;

        billows = mix(billows, clamp(Remap(shape.r, fbm - 1.0, 1.0, 0.0, 1.0), 0.0, 1.0), shapeWeight);

    }

    float density = clamp(Remap(billows * gradient, 1.0 - weather, 1.0, 0.0, 1.0), 0.0, 1.0);

    if (density <= 0.0 || detailWeight <= 0.0) {

        s.extinction = density * v.medium.w;

        return s;

    }

    // Wispy at the base, billowy towards the top.
    vec4 detail = texture(detailNoise, body * v.params2.w + vec3(v.params2.y, 0.0, 0.0));
    float wisps = detail.r * 0.625 + detail.g * 0.25 + detail.b * 0.125;

    wisps = mix(wisps, 1.0 - wisps, clamp(h * 4.0, 0.0, 1.0));
    density = clamp(Remap(density, wisps * 0.3 * detailWeight, 1.0, 0.0, 1.0), 0.0, 1.0);

    s.extinction = density * v.medium.w;

    return s;

}

VolumeSample SampleVolume(VolumePrimitive v, vec3 p) {

    if (v.kind.x == VolumeCloudLayer) {

        return CloudLayer(v, p);

    }

    if (v.kind.x == VolumeNoiseSphere) {

        return NoiseSphere(v, p);

    }

    if (v.kind.x == VolumePlume) {

        return Plume(v, p);

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
    float reach = v.kind.x == VolumeCloudLayer ? v.params1.x - v.params0.w : v.position.w * 2.0;
    float dt = reach / float(max(steps, 1));
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

    float pixelAngle = 4.0 / (abs(frame.projection[1][1]) * frame.viewport.y);

    // Clouds step no finer than they need: 60 m up close, about a pixel's footprint further away.
    float stepLength = max(60.0, enter * pixelAngle * 0.35);
    int steps = v.kind.x == VolumeCloudLayer ? clamp(int((exit - enter) / stepLength), 24, v.kind.y) : max(v.kind.y, 1);
    float dt = (exit - enter) / float(steps);
    vec3 sunDirection = frame.sunDirection.xyz;
    float cosTheta = dot(direction, sunDirection);
    float g = v.emission.w;
    vec3 albedo = v.medium.rgb;

    for (int i = 0; i < steps; i++) {

        float t = enter + (float(i) + jitter) * dt;

        volumeFootprint = t * pixelAngle;
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

        for (int octave = 0; octave < 4; octave++) {

            scattered += a * sun * exp(-shadow * b) * mix(HenyeyGreenstein(cosTheta, g * c), HenyeyGreenstein(cosTheta, -0.3 * c), 0.25);
            a *= 0.7;
            b *= 0.3;
            c *= 0.4;

        }

        // Point lights scatter isotropically, unshadowed.
        for (int light = 0; light < int(frame.lightCount.x); light++) {

            vec3 toLight = frame.lightPositions[light].xyz - p;
            float distanceSquared = dot(toLight, toLight) + frame.lightPositions[light].w * frame.lightPositions[light].w;

            ambient += frame.lightIntensities[light].rgb / distanceSquared / (4.0 * PI);

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

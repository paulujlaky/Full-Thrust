// Participating media shared by every volume: primitives, density by kind, and lighting. Positions are render-local metres.

const int VolumeNoiseSphere = 1;
const int VolumePlume = 2;
const int VolumeCloudLayer = 3;
const int VolumeTrail = 4;
const int VolumeShock = 5;
const int VolumeGroundOutflow = 6;
const int VolumeCirrus = 7;

// Metres per period of the noise in exhaust trails; matches TrailSource.NoiseScale.
const float TrailNoiseScale = 40.0;
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

bool PlumeSegment(VolumePrimitive v, vec3 origin, vec3 direction, out float enter, out float exit) {

    vec3 offset = origin - v.params0.xyz;
    float h = dot(offset, v.axis.xyz);
    float dh = dot(direction, v.axis.xyz);
    enter = 0.0;
    exit = 1e30;

    if (abs(dh) > 1e-6) {

        float nearCap = -h / dh;
        float farCap = (v.axis.w - h) / dh;
        enter = max(enter, min(nearCap, farCap));
        exit = max(nearCap, farCap);

    } else if (h < 0.0 || h > v.axis.w) {

        return false;

    }

    if (exit <= enter) {

        return false;

    }

    // Outside two Gaussian radii the jet contributes less than a ten-thousandth of its centre.
    float spread = 2.0 * (v.params1.x + 0.02);
    float radius = 2.0 * v.params0.w + spread * h;
    vec3 radial = offset - v.axis.xyz * h;
    vec3 ray = direction - v.axis.xyz * dh;
    float growth = spread * dh;
    float a = dot(ray, ray) - growth * growth;
    float b = dot(ray, radial) - growth * radius;
    float c = dot(radial, radial) - radius * radius;

    if (abs(a) < 1e-6) {

        if (abs(b) < 1e-6) {

            return c <= 0.0;

        }

        float hit = -0.5 * c / b;
        if (b > 0.0) {

            exit = min(exit, hit);

        } else {

            enter = max(enter, hit);

        }

        return exit > enter;

    }

    float discriminant = b * b - a * c;
    if (discriminant <= 0.0) {

        return a < 0.0;

    }

    float root = sqrt(discriminant);
    float first = (-b - root) / a;
    float second = (-b + root) / a;

    if (a > 0.0) {

        enter = max(enter, first);
        exit = min(exit, second);

    } else if (dh > 0.0) {

        enter = max(enter, first);

    } else {

        exit = min(exit, second);

    }

    return exit > enter;

}

bool VolumeSegment(VolumePrimitive v, vec3 origin, vec3 direction, out float enter, out float exit) {

    float near;
    float far;

    if (v.kind.x == VolumePlume) {

        return PlumeSegment(v, origin, direction, enter, exit);

    }

    if (v.kind.x == VolumeCloudLayer || v.kind.x == VolumeCirrus) {

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

    if (v.kind.w == 1) {

        float lobe = smoothstep(0.30, 0.72, shape.g);
        float shell = radial + 0.44 * (0.5 - lobe) + 0.19 * (erosion - 0.5);
        float entrained = smoothstep(0.18, 0.68, shape.r * (0.48 + 0.67 * lobe) - 0.16 * erosion);
        density = (1.0 - smoothstep(0.32, 0.89, shell)) * entrained;

    }

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

    vec3 transverse = offset - v.axis.xyz * x;
    float r = length(transverse);
    float radius = exitRadius + x * (v.params1.x + 0.02);
    float dilution = exitRadius * exitRadius / (radius * radius);
    float spacing = max(v.params1.y, 1e-3);

    if (v.kind.w != 0 && x < exitRadius * 12.0) {

        float ring = length(v.position.xyz);
        vec3 u = v.position.xyz / ring;
        vec3 w = cross(v.axis.xyz, u);
        float pitch = 2.0 * PI / float(abs(v.kind.w));
        float theta = atan(dot(transverse, w), dot(transverse, u));
        float angle = floor(theta / pitch + 0.5) * pitch;
        float bellDistance = length(transverse - ring * (u * cos(angle) + w * sin(angle)));
        float separate = 1.0 - smoothstep(exitRadius, exitRadius * 12.0, x);
        float bellRadius = exitRadius - ring;
        r = mix(r, v.kind.w > 0 ? min(r, bellDistance) : bellDistance, separate);
        radius = mix(radius, bellRadius + x * (v.params1.x + 0.055), separate);
        spacing *= mix(1.0, bellRadius / exitRadius, separate);

    }

    vec3 flow = offset / (exitRadius * 4.0) - v.axis.xyz * (frame.exposure.w * 6.0);
    vec4 eddy = texture(shapeNoise, flow);
    float mixing = smoothstep(0.0, span * 0.35, x);
    float edge = r / radius + mixing * (eddy.r - 0.5) * 1.35;
    float profile = exp(-2.5 * edge * edge);
    float along = (1.0 - x / span) * (1.0 - x / span) * smoothstep(0.0, exitRadius * 0.3, x);

    // Turbulence streams downstream with the flow and grows with distance from the nozzle.
    float turbulence = mix(1.0, eddy.g * 1.8, mixing);

    float density = profile * dilution * along * turbulence;

    float wave = 0.5 + 0.5 * cos(6.2831853 * x / spacing);
    float shockRadius = radius * (0.16 + 0.55 * wave);
    float shockWall = (r - shockRadius) / max(0.22 * radius, 1e-4);
    float diamonds = v.params1.z * exp(-shockWall * shockWall - x / (spacing * 2.6)) * along;

    // A white-hot core near the nozzle inside a cooler sheath; params2 holds the sheath colour and core whiteness.
    vec3 white = vec3(dot(v.emission.rgb, vec3(0.2126, 0.7152, 0.0722)));
    float core = exp(-4.0 * (r * r) / (radius * radius)) * exp(-x / (span * 0.3));
    vec3 tint = mix(vec3(1.0), v.params2.rgb, smoothstep(0.3, 1.0, r / radius));
    vec3 hot = mix(v.emission.rgb * tint, white * 1.3, core * v.params2.w) * (0.3 + 0.7 * exp(-x / (span * 0.25)));

    s.extinction = density * v.medium.w;
    // Afterburning keeps the exhaust glowing as it spreads, so emission thins more slowly than mass.
    float cooling = pow(dilution, mix(0.5, 1.13, v.params1.w));
    s.emission = hot * profile * cooling * along * turbulence + mix(v.emission.rgb, white, 0.6) * diamonds * 0.85;

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

    // Coarse towers: their height gives the tops relief that a low sun picks out, even from orbit.
    vec3 cell = body * v.params2.z;
    vec3 driftField = texture(shapeNoise, cell.zxy * 0.0193 + vec3(0.13, 0.37, 0.71)).gba - 0.5;
    vec4 coarse = texture(shapeNoise, cell * 0.125 + 0.37 + 0.83 * driftField);
    float towers = smoothstep(0.32, 0.80, coarse.r);

    // Flat bases and rounded tops that rise where the weather is densest.
    float top = (0.35 + 0.65 * weather) * (0.6 + 0.4 * towers);
    float base = 0.10 * smoothstep(0.2, 0.8, coarse.b) * (1.0 - 0.6 * weather);
    float gradient = smoothstep(base, base + 0.08, h) * clamp(Remap(h, top * 0.6, top, 1.0, 0.0), 0.0, 1.0);

    if (gradient <= 0.0) {

        return s;

    }

    // Far away the billows fade to a value that keeps about the same area covered.
    float shapeWeight = 1.0 - smoothstep(0.1, 0.4, volumeFootprint * v.params2.z);
    float detailWeight = 1.0 - smoothstep(0.1, 0.4, volumeFootprint * v.params2.w);
    float billows = 0.55 + 0.45 * towers;

    if (shapeWeight > 0.0) {

        vec4 shape = texture(shapeNoise, cell + 1.8 * driftField + 0.65 * (coarse.gba - 0.5) + vec3(v.params2.x, 0.0, 0.0));
        float fbm = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;

        // The billows tile every shape period, so in thin weather their few peaks line up; cells of the coarse field, a period across, decide which rise.
        float rise = 0.85 + 0.3 * coarse.b;

        billows = mix(billows, clamp(Remap(shape.r * rise, fbm - 1.0, 1.0, 0.0, 1.0), 0.0, 1.0), shapeWeight);

    }

    float density = clamp(Remap(billows * gradient, 1.0 - weather, 1.0, 0.0, 1.0), 0.0, 1.0);
    density *= mix(0.18, 1.0, smoothstep(0.18, 0.80, coarse.g)) * sqrt(weather);

    if (density <= 0.0 || detailWeight <= 0.0) {

        float extinction = density * v.medium.w;
        // A distant pixel sees clear gaps between opaque billows; average their transmission.
        float path = clamp(volumeFootprint, 1.0, 1300.0);
        float fill = 0.32 + 0.63 * smoothstep(0.02, 0.54, density);
        float filtered = -log(max(0.0001, mix(1.0, exp(-extinction * path), fill))) / path;
        s.extinction = mix(extinction, filtered, smoothstep(240.0, 1400.0, volumeFootprint));

        return s;

    }

    // Wispy at the base, billowy towards the top.
    vec4 detail = texture(detailNoise, body.zxy * v.params2.w + 2.3 * coarse.bag + vec3(v.params2.y, 0.0, 0.0));
    float wisps = detail.r * 0.625 + detail.g * 0.25 + detail.b * 0.125;

    wisps = mix(wisps, 1.0 - wisps, clamp(h * 4.0, 0.0, 1.0));
    density = clamp(Remap(density, wisps * 0.3 * detailWeight, 1.0, 0.0, 1.0), 0.0, 1.0);

    s.extinction = density * v.medium.w;

    return s;

}

// Exhaust trail capsule: params0 and params1 the older and newer ends with radii, medium.w and params2.w their extinctions, axis turns into the body-fixed noise placed by params2.xyz.
VolumeSample Trail(VolumePrimitive v, vec3 p) {

    VolumeSample s;

    s.extinction = 0.0;
    s.emission = vec3(0.0);

    vec3 a = v.params0.xyz;
    vec3 ab = v.params1.xyz - a;
    float t = clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-6), 0.0, 1.0);
    vec3 coord = QuaternionRotate(v.axis, p - a) / TrailNoiseScale + v.params2.xyz;

    // Slow swells bulge the column into the puffs a rising exhaust cloud breaks into.
    float swell = 0.7 + 0.6 * texture(shapeNoise, coord * 0.3).g;
    float radial = length(p - a - ab * t) / (mix(v.params0.w, v.params1.w, t) * swell);

    if (radial >= 1.0) {

        return s;

    }

    float shape = texture(shapeNoise, coord).r;
    float erosion = texture(detailNoise, coord * 3.0).r;

    float base = (1.0 - smoothstep(0.15, 1.0, radial)) * (0.3 + 0.7 * shape);
    float density = clamp(Remap(base, 0.15 + 0.35 * erosion, 1.0, 0.0, 1.0), 0.0, 1.0);

    s.extinction = density * mix(v.medium.w, v.params2.w, t);

    return s;

}

// Bow shock: a glowing paraboloid swept along the windward line from params0 (near end, nose radius) to params1 (far end, its relative glow), opening down axis.xyz for axis.w metres.
VolumeSample Shock(VolumePrimitive v, vec3 p) {

    VolumeSample s;

    s.extinction = 0.0;
    s.emission = vec3(0.0);

    vec3 span = v.params1.xyz - v.params0.xyz;
    vec3 across = span - v.axis.xyz * dot(span, v.axis.xyz);
    float reach = dot(across, across);

    // Measured from the stretch of the line level with this point across the flow, so the sheath is one surface rather than a row of caps.
    float u = reach > 1e-4 ? clamp(dot(p - v.params0.xyz, across) / reach, 0.0, 1.0) : 0.0;

    vec3 offset = p - v.params0.xyz - span * u;
    float nose = v.params0.w;
    float x = dot(offset, v.axis.xyz);

    if (x < -nose || x > v.axis.w) {

        return s;

    }

    float r = length(offset - v.axis.xyz * x);
    float slope = r / nose;
    float behind = (x - 0.5 * r * slope) * inversesqrt(1.0 + slope * slope);
    float thickness = nose * 0.12 + max(x, 0.0) * 0.055;

    // The shock front glows hardest, and the compressed gas behind it fades downstream; the flow tears it into flickering streaks.
    float sheet = exp(-pow(behind / thickness, 2.0));
    float inside = smoothstep(-thickness, thickness, behind) * exp(-max(behind, 0.0) / (nose * 0.34));
    float streaks = texture(shapeNoise, vec3(r / (nose * 1.5), x / (nose * 6.0) - frame.exposure.w * 2.0, 0.37 + u * sqrt(reach) / (nose * 4.0))).r;

    // Hottest in the layer pressed against the nose where the shock stands normal; as it turns oblique down the flanks it barely glows.
    float fade = 1.0 - smoothstep(v.axis.w * 0.5, v.axis.w, x);
    float cooling = exp(-max(x, 0.0) / (nose * 0.74));
    float glow = (sheet + 0.38 * inside) * cooling * fade * (0.35 + streaks) * mix(1.0, v.params1.w, u);

    s.emission = v.emission.rgb * mix(vec3(1.0, 0.38, 0.16), vec3(1.0), cooling) * glow;
    s.extinction = v.medium.w * glow;

    return s;

}

VolumeSample SampleVolume(VolumePrimitive v, vec3 p) {

    if (v.kind.x == VolumeCirrus) {

        vec3 body = QuaternionRotate(v.axis, p - v.params0.xyz);
        vec3 cell = body * v.params1.z;
        vec4 warp = texture(shapeNoise, cell * 0.036);
        vec3 streak = cell * vec3(1.7, 7.0, 9.0) + 2.5 * warp.rgb + vec3(v.params1.w, 0.0, 0.0);
        vec4 systems = WeatherSystems(body);
        float weather = sqrt(systems.a) * smoothstep(0.32, 0.68, warp.g) * v.params1.y;
        float fibres = texture(shapeNoise, streak).r;
        float fine = texture(detailNoise, streak * 2.1).g;
        float density = weather * smoothstep(0.31, 0.78, fibres * 0.78 + fine * 0.22);

        return VolumeSample(v.medium.w * density, vec3(0.0));

    }

    if (v.kind.x == VolumeShock) {

        return Shock(v, p);

    }

    if (v.kind.x == VolumeTrail) {

        return Trail(v, p);

    }

    if (v.kind.x == VolumeCloudLayer) {

        return CloudLayer(v, p);

    }

    if (v.kind.x == VolumeNoiseSphere) {

        return NoiseSphere(v, p);

    }

    if (v.kind.x == VolumeGroundOutflow) {

        vec3 local = (p - v.position.xyz) / v.position.w;
        float height = dot(local, v.axis.xyz);
        vec3 lateral = local - height * v.axis.xyz;
        float reach = length(lateral);
        float time = frame.exposure.w;
        vec3 outward = lateral / max(reach, 0.05);
        // Advect through a bounded phase so the billows keep their spatial frequency.
        vec3 noiseUv = local * 1.9 - outward * 0.16 * sin(time * 1.5 - reach * 9.0) - v.axis.xyz * time * 0.11;
        vec4 lobes = texture(shapeNoise, noiseUv);
        float wisps = texture(detailNoise, noiseUv * 3.4).r;
        float swell = smoothstep(0.28, 0.78, lobes.r * 0.80 + 0.20 * wisps);
        float top = (0.045 + 0.36 * swell) * smoothstep(0.0, 0.4, reach);
        float edge = 1.0 - smoothstep(0.44 + 0.24 * lobes.b, 0.73 + 0.22 * lobes.g, reach);
        float layer = (1.0 - smoothstep(top * 0.18, max(top, 0.01), height)) * step(-0.012, height);

        return VolumeSample(v.medium.w * edge * layer * (0.15 + 0.85 * swell), vec3(0.0));

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

    // Emission also carries a depth when the gas is optically thin.
    float depth;
    float depthWeight;

};

// Marches one primitive between enter and exit, adding to the running result. Luminance is before exposure.
void MarchVolume(VolumePrimitive v, vec3 origin, vec3 direction, float enter, float exit, float jitter, inout VolumeResult result) {

    float pixelAngle = 4.0 / (abs(frame.projection[1][1]) * frame.viewport.y);

    // Clouds step no finer than they need: 60 m up close, about a pixel's footprint further away.
    float stepLength = max(60.0, enter * pixelAngle * 0.35);
    int steps = v.kind.x == VolumeCloudLayer ? clamp(int((exit - enter) / stepLength), 24, v.kind.y) : max(v.kind.y, 1);
    float stepSpan = (exit - enter) / float(steps);
    float growth = 1.0;

    if (v.kind.x == VolumePlume) {

        float startHeight = max(0.0, dot(origin - v.params0.xyz + direction * enter, v.axis.xyz));
        float endHeight = max(0.0, dot(origin - v.params0.xyz + direction * exit, v.axis.xyz));
        float spread = v.params1.x + 0.02;
        float ratio = (v.params0.w + spread * endHeight) / (v.params0.w + spread * startHeight);
        if (abs(ratio - 1.0) > 0.06) {

            // Short steps resolve the hot throat even when the same ray crosses a long vacuum fan.
            growth = pow(ratio, 1.0 / float(steps));
            stepSpan = (exit - enter) * (growth - 1.0) / (ratio - 1.0);

        }

    }

    float stepStart = enter;
    vec3 sunDirection = frame.sunDirection.xyz;
    float cosTheta = dot(direction, sunDirection);
    float g = v.emission.w;
    vec3 albedo = v.medium.rgb;
    float shadow = 0.0;
    float shadowAt = -1e30;

    for (int i = 0; i < steps; i++) {

        float dt = stepSpan;
        float t = stepStart + jitter * dt;
        stepStart += dt;
        stepSpan *= growth;

        volumeFootprint = t * pixelAngle;
        vec3 p = origin + direction * t;
        VolumeSample s = SampleVolume(v, p);

        if (s.extinction <= 1e-6 && dot(s.emission, s.emission) <= 0.0) {

            continue;

        }

        if (s.extinction <= 1e-6) {

            vec3 light = result.transmittance * s.emission * dt;
            float weight = min(dot(light, vec3(0.2126, 0.7152, 0.0722)) * frame.exposure.x, result.transmittance);
            result.luminance += light;
            result.depth += weight * t;
            result.depthWeight += weight;

            continue;

        }

        vec3 sun = SunIlluminanceAt(p);
        // Shadow variation below a volume pixel is unresolved; nearby march steps can share it.
        if (v.kind.x != VolumeCloudLayer || t - shadowAt >= min(volumeFootprint, 650.0)) {

            shadow = SunOpticalDepth(v, p, sunDirection);
            shadowAt = t;

        }
        vec3 up = normalize(PlanetRelative(p));
        vec3 ambient = (AmbientIrradiance(p, up) + AmbientIrradiance(p, -up)) / (4.0 * PI) * EclipseSkylight(p);

        // Multiple scattering as fading octaves of the single-scattering term (Wrenninge 2013).
        vec3 scattered = vec3(0.0);
        float a = 1.0;
        float b = 1.0;
        float c = 1.0;

        for (int octave = 0; octave < 4; octave++) {

            // Beer-Powder (Schneider 2015) on the first octave: thin, sun-facing edges darken, sculpting the billows.
            float attenuation = exp(-shadow * b);
            float powder = octave == 0 ? mix(1.0, 2.0 * (1.0 - exp(-2.0 * shadow)), 0.5) : 1.0;

            scattered += a * sun * attenuation * powder * mix(HenyeyGreenstein(cosTheta, g * c), HenyeyGreenstein(cosTheta, -0.3 * c), 0.25);
            a *= 0.7;
            b *= 0.3;
            c *= 0.4;

        }

        // Point lights scatter isotropically, unshadowed.
        for (int light = 0; light < int(frame.lightCount.x); light++) {

            vec3 toLight = frame.lightPositions[light].xyz - p;
            float distanceSquared = dot(toLight, toLight) + frame.lightPositions[light].w * frame.lightPositions[light].w;

            float beam = BeamWeight(light, toLight * inversesqrt(max(dot(toLight, toLight), 1e-6)));
            ambient += frame.lightIntensities[light].rgb * beam / distanceSquared / (4.0 * PI);

        }

        vec3 source = albedo * s.extinction * (scattered + ambient) + s.emission;
        float stepTransmittance = exp(-s.extinction * dt);

        // Light added across the step; in a nearly clear glowing gas it tends to the step length rather than vanishing.
        float span = s.extinction * dt > 1e-4 ? (1.0 - stepTransmittance) / s.extinction : dt;

        result.luminance += result.transmittance * source * span;
        float emitted = dot(s.emission, vec3(0.2126, 0.7152, 0.0722)) * span * frame.exposure.x;
        float weight = result.transmittance * max(1.0 - stepTransmittance, min(emitted, 1.0));
        result.depth += weight * t;
        result.depthWeight += weight;
        result.transmittance *= stepTransmittance;

        if (result.transmittance < 0.005) {

            break;

        }

    }

}

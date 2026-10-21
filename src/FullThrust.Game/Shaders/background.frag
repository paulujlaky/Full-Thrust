#version 450

#include "common.glsl"

layout(location = 0) in vec2 vUv;

// Mirrors BackgroundPush in Rendering/BackgroundPass.cs.
layout(push_constant) uniform Push {

    // x star brightness scale, y Milky Way brightness scale, z angular size of a pixel in radians, w faintest magnitude.
    vec4 params;

} pc;

layout(location = 0) out vec4 outColor;

// Illuminance of a magnitude 0 star, in lux.
const float ZeroMagnitudeLux = 2.54e-6;

// Ecliptic world frame (Y north, X towards the equinox) to J2000 equatorial (Z north).
const float Obliquity = 0.40910518;

uvec3 Pcg3(uvec3 v) {

    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;

    return v;

}

vec3 Random3(uvec3 seed) {

    return vec3(Pcg3(seed) & 0xFFFFFFu) / 16777215.0;

}

vec3 ToEquatorial(vec3 world) {

    vec3 ecliptic = vec3(world.x, -world.z, world.y);
    float c = cos(Obliquity);
    float s = sin(Obliquity);

    return vec3(ecliptic.x, ecliptic.y * c - ecliptic.z * s, ecliptic.y * s + ecliptic.z * c);

}

vec3 FromRaDec(float raDeg, float decDeg) {

    float ra = radians(raDeg);
    float dec = radians(decDeg);

    return vec3(cos(dec) * cos(ra), cos(dec) * sin(ra), sin(dec));

}

// Cube-face cell coordinates for a direction: face index and position on it in [0, 1).
vec3 CubeFace(vec3 d) {

    vec3 a = abs(d);

    if (a.x >= a.y && a.x >= a.z) {

        return vec3(d.x > 0.0 ? 0.0 : 1.0, vec2(d.y, d.z) / a.x * 0.5 + 0.5);

    }

    if (a.y >= a.z) {

        return vec3(d.y > 0.0 ? 2.0 : 3.0, vec2(d.x, d.z) / a.y * 0.5 + 0.5);

    }

    return vec3(d.z > 0.0 ? 4.0 : 5.0, vec2(d.x, d.y) / a.z * 0.5 + 0.5);

}

vec3 FromCubeFace(float face, vec2 uv) {

    vec2 p = uv * 2.0 - 1.0;

    if (face < 0.5) return normalize(vec3(1.0, p.x, p.y));
    if (face < 1.5) return normalize(vec3(-1.0, p.x, p.y));
    if (face < 2.5) return normalize(vec3(p.x, 1.0, p.y));
    if (face < 3.5) return normalize(vec3(p.x, -1.0, p.y));
    if (face < 4.5) return normalize(vec3(p.x, p.y, 1.0));

    return normalize(vec3(p.x, p.y, -1.0));

}

// Blackbody-ish tint from a 0..1 temperature parameter, red dwarfs to blue giants.
vec3 StarColor(float t) {

    vec3 cool = vec3(1.0, 0.62, 0.38);
    vec3 sunlike = vec3(1.0, 0.93, 0.84);
    vec3 hot = vec3(0.68, 0.78, 1.0);

    return t < 0.6 ? mix(cool, sunlike, t / 0.6) : mix(sunlike, hot, (t - 0.6) / 0.4);

}

float GalacticLatitude(vec3 equatorial) {

    return asin(clamp(dot(equatorial, FromRaDec(192.85948, 27.12825)), -1.0, 1.0));

}

// Stars on three cube grids, one per magnitude range, with real-sky counts (N(<m) ~ 10^(0.47 m)).
vec3 Stars(vec3 equatorial) {

    vec3 sum = vec3(0.0);
    float pixel = pc.params.z;
    float plane = exp(-pow(GalacticLatitude(equatorial) / 0.35, 2.0));

    const float resolutions[3] = float[](24.0, 96.0, 384.0);
    const float occupancies[3] = float[](0.25, 0.27, 0.35);
    const float brightest[3] = float[](-1.5, 4.0, 7.0);
    const float faintest[3] = float[](4.0, 7.0, 10.0);

    vec3 face = CubeFace(equatorial);

    for (int layer = 0; layer < 3; layer++) {

        float resolution = resolutions[layer];
        vec2 cell = floor(face.yz * resolution);
        vec3 r = Random3(uvec3(uvec2(cell), uint(face.x) * 7u + uint(layer) * 131u));

        float occupancy = occupancies[layer] * (layer == 0 ? 1.0 : 0.6 + 0.8 * plane);

        if (r.x > occupancy) {

            continue;

        }

        float low = pow(10.0, 0.47 * brightest[layer]);
        float high = pow(10.0, 0.47 * faintest[layer]);
        float magnitude = log(mix(low, high, r.y)) / (0.47 * log(10.0));

        if (magnitude > pc.params.w) {

            continue;

        }

        vec2 jitter = 0.2 + 0.6 * Random3(uvec3(uvec2(cell), uint(layer) + 977u)).xy;
        vec3 star = FromCubeFace(face.x, (cell + jitter) / resolution);

        float angle = acos(clamp(dot(star, equatorial), -1.0, 1.0));
        float sigma = pixel * 0.5;
        float weight = exp(-0.5 * angle * angle / (sigma * sigma)) / (6.2831853 * sigma * sigma);

        float lux = ZeroMagnitudeLux * pow(10.0, -0.4 * magnitude);

        sum += StarColor(r.z) * lux * weight;

    }

    return sum * pc.params.x;

}

float ValueNoise3(vec3 p) {

    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    uvec3 c = uvec3(ivec3(i) + 4096);

    float n000 = Random3(c).x;
    float n100 = Random3(c + uvec3(1, 0, 0)).x;
    float n010 = Random3(c + uvec3(0, 1, 0)).x;
    float n110 = Random3(c + uvec3(1, 1, 0)).x;
    float n001 = Random3(c + uvec3(0, 0, 1)).x;
    float n101 = Random3(c + uvec3(1, 0, 1)).x;
    float n011 = Random3(c + uvec3(0, 1, 1)).x;
    float n111 = Random3(c + uvec3(1, 1, 1)).x;

    return mix(mix(mix(n000, n100, u.x), mix(n010, n110, u.x), u.y), mix(mix(n001, n101, u.x), mix(n011, n111, u.x), u.y), u.z);

}

float Fbm(vec3 p) {

    float sum = 0.0;
    float amplitude = 0.5;

    for (int i = 0; i < 5; i++) {

        sum += ValueNoise3(p) * amplitude;
        p *= 2.03;
        amplitude *= 0.5;

    }

    return sum;

}

// The Milky Way: a diffuse band about the galactic plane, brightest towards the centre, crossed by dust lanes.
vec3 MilkyWay(vec3 equatorial) {

    float latitude = GalacticLatitude(equatorial);
    float toCentre = dot(equatorial, FromRaDec(266.405, -28.936)) * 0.5 + 0.5;

    float band = exp(-pow(latitude / (0.12 + 0.1 * toCentre), 2.0));
    float bulge = exp(-pow(latitude / 0.22, 2.0)) * pow(toCentre, 6.0);

    float clouds = Fbm(equatorial * 6.0);
    float dust = smoothstep(0.45, 0.75, Fbm(equatorial * 11.0 + 3.1)) * exp(-pow(latitude / 0.05, 2.0));

    // About 2e-4 cd/m2 at its brightest, which is how dim the real band is.
    float luminance = 2.0e-4 * (band * (0.35 + 0.65 * clouds) + bulge * 1.5) * (1.0 - 0.8 * dust);

    return luminance * mix(vec3(0.75, 0.82, 1.0), vec3(1.0, 0.88, 0.72), toCentre) * pc.params.y;

}

// Solar disc with limb darkening, normalised so the disc delivers the sun's illuminance.
vec3 SunDisc(vec3 direction) {

    float cosRadius = frame.sunDirection.w;
    float cosAngle = dot(direction, frame.sunDirection.xyz);

    if (cosAngle < cosRadius) {

        return vec3(0.0);

    }

    float solidAngle = 6.2831853 * (1.0 - cosRadius);
    float radius = acos(cosRadius);
    float r = acos(clamp(cosAngle, -1.0, 1.0)) / max(radius, 1e-6);
    float mu = sqrt(max(1.0 - r * r, 0.0));
    float limb = (1.0 - 0.6 * (1.0 - mu)) / (1.0 - 0.6 / 3.0);

    return frame.sunIlluminance.rgb / solidAngle * limb;

}

void main() {

    vec3 direction = ViewDirection(vUv * 2.0 - 1.0);
    vec3 equatorial = ToEquatorial(direction);

    vec3 sky = vec3(0.0);
    vec3 transmittance = vec3(1.0);

    if (AtmosphereEnabled()) {

        vec3 origin = PlanetRelative(frame.cameraPosition.xyz);
        float r = length(origin);
        bool ground = HitsGround(origin, direction);

        // Inside the atmosphere the sky-view table is accurate enough; from space each pixel is marched.
        if (r < TopRadius()) {

            sky = SkyViewLuminance(origin, direction, frame.sunDirection.xyz);
            transmittance = ground ? vec3(0.0) : Transmittance(r, dot(direction, origin / r));

        } else {

            ScatteringResult result = IntegrateScattering(origin, direction, frame.sunDirection.xyz, 32, 1e12);

            sky = result.luminance;
            transmittance = ground ? vec3(0.0) : result.transmittance;

        }

        sky *= frame.sunIlluminance.rgb;

    }

    vec3 radiance = sky + (Stars(equatorial) + MilkyWay(equatorial) + SunDisc(direction)) * transmittance;

    outColor = vec4(Expose(radiance), 1.0);

}

// Atmospheric scattering after Hillaire 2020; positions are metres relative to the planet centre.

const vec2 TransmittanceLutSize = vec2(256.0, 64.0);
const vec2 MultiScatterLutSize = vec2(32.0, 32.0);
const vec2 SkyViewLutSize = vec2(192.0, 108.0);
const vec2 IrradianceLutSize = vec2(64.0, 16.0);

bool AtmosphereEnabled() {

    return frame.atmosphereShape.z > 0.5;

}

float BottomRadius() {

    return frame.atmosphereCenter.w;

}

float TopRadius() {

    return frame.atmosphereShape.x;

}

vec3 PlanetRelative(vec3 worldPosition) {

    return worldPosition - frame.atmosphereCenter.xyz;

}

struct Medium {

    vec3 rayleighScattering;
    vec3 mieScattering;
    vec3 scattering;
    vec3 extinction;

};

Medium SampleMedium(float height) {

    float rayleighDensity = exp(-max(height, 0.0) / frame.rayleighScattering.w);
    float mieDensity = exp(-max(height, 0.0) / frame.mieScattering.w);
    float ozoneDensity = max(0.0, 1.0 - abs(height - frame.ozoneAbsorption.w) / frame.atmosphereShape.w);

    Medium m;

    m.rayleighScattering = frame.rayleighScattering.rgb * rayleighDensity;
    m.mieScattering = frame.mieScattering.rgb * mieDensity;
    m.scattering = m.rayleighScattering + m.mieScattering;
    m.extinction = m.rayleighScattering + frame.mieExtinction.rgb * mieDensity + frame.ozoneAbsorption.rgb * ozoneDensity;

    return m;

}

float RayleighPhase(float cosTheta) {

    return 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);

}

float MiePhase(float cosTheta) {

    float g = frame.atmosphereShape.y;
    float g2 = g * g;

    return 3.0 / (8.0 * PI) * (1.0 - g2) * (1.0 + cosTheta * cosTheta) / ((2.0 + g2) * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));

}

// Both intersections of a ray with a sphere about the origin; false when it misses.
bool RaySphere(vec3 origin, vec3 direction, float radius, out float near, out float far) {

    float b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;
    float discriminant = b * b - c;

    if (discriminant < 0.0) {

        near = -1.0;
        far = -1.0;

        return false;

    }

    float s = sqrt(discriminant);

    near = -b - s;
    far = -b + s;

    return true;

}

bool HitsGround(vec3 origin, vec3 direction) {

    float near;
    float far;

    return RaySphere(origin, direction, BottomRadius(), near, far) && far > 0.0;

}

// Keeps LUT lookups on texel centres so the edges are not filtered against the clamp border.
float UnitToSubUv(float u, float size) {

    return (u + 0.5 / size) * (size / (size + 1.0));

}

float SubUvToUnit(float u, float size) {

    return (u - 0.5 / size) * (size / (size - 1.0));

}

vec2 TransmittanceUv(float viewHeight, float viewZenithCos) {

    float bottom = BottomRadius();
    float top = TopRadius();
    float h = sqrt(max(top * top - bottom * bottom, 0.0));
    float rho = sqrt(max(viewHeight * viewHeight - bottom * bottom, 0.0));

    float discriminant = viewHeight * viewHeight * (viewZenithCos * viewZenithCos - 1.0) + top * top;
    float d = max(0.0, -viewHeight * viewZenithCos + sqrt(max(discriminant, 0.0)));
    float dMin = top - viewHeight;
    float dMax = rho + h;

    return vec2((d - dMin) / max(dMax - dMin, 1e-3), rho / h);

}

void TransmittanceParams(vec2 uv, out float viewHeight, out float viewZenithCos) {

    float bottom = BottomRadius();
    float top = TopRadius();
    float h = sqrt(max(top * top - bottom * bottom, 0.0));
    float rho = h * uv.y;

    viewHeight = sqrt(rho * rho + bottom * bottom);

    float dMin = top - viewHeight;
    float dMax = rho + h;
    float d = dMin + uv.x * (dMax - dMin);

    viewZenithCos = d == 0.0 ? 1.0 : clamp((h * h - rho * rho - d * d) / (2.0 * viewHeight * d), -1.0, 1.0);

}

// Transmittance from a point to the top of the atmosphere; ignores the ground, which callers test for.
vec3 Transmittance(float radius, float zenithCos) {

    if (radius >= TopRadius()) {

        return vec3(1.0);

    }

    return texture(transmittanceLut, TransmittanceUv(max(radius, BottomRadius() + 1.0), zenithCos)).rgb;

}

vec3 MultipleScattering(float radius, float sunZenithCos) {

    vec2 uv = clamp(vec2(sunZenithCos * 0.5 + 0.5, (radius - BottomRadius()) / (TopRadius() - BottomRadius())), 0.0, 1.0);

    return texture(multiScatterLut, vec2(UnitToSubUv(uv.x, MultiScatterLutSize.x), UnitToSubUv(uv.y, MultiScatterLutSize.y))).rgb;

}

vec2 IrradianceUv(float radius, float sunZenithCos) {

    vec2 uv = clamp(vec2(sunZenithCos * 0.5 + 0.5, (radius - BottomRadius()) / (TopRadius() - BottomRadius())), 0.0, 1.0);

    return vec2(UnitToSubUv(uv.x, IrradianceLutSize.x), UnitToSubUv(uv.y, IrradianceLutSize.y));

}

// Fraction of the solar disc above the geometric horizon at this radius.
float SunVisibility(float radius, float sunZenithCos) {

    float sinHorizon = BottomRadius() / max(radius, BottomRadius());
    float cosHorizon = -sqrt(max(1.0 - sinHorizon * sinHorizon, 0.0));
    float angularRadius = acos(frame.sunDirection.w);

    return smoothstep(-angularRadius, angularRadius, sunZenithCos - cosHorizon);

}

struct ScatteringResult {

    vec3 luminance;
    vec3 transmittance;

};

// One segment of length dt around p, added to a running integral.
void ScatterStep(vec3 p, float dt, vec3 sunDirection, float rayleighPhase, float miePhase, inout ScatteringResult result) {

    float r = length(p);
    float sunCos = dot(sunDirection, p / r);

    Medium m = SampleMedium(r - BottomRadius());
    vec3 stepTransmittance = exp(-m.extinction * dt);

    vec3 sun = Transmittance(r, sunCos) * SunVisibility(r, sunCos);
    vec3 source = sun * (m.rayleighScattering * rayleighPhase + m.mieScattering * miePhase) + MultipleScattering(r, sunCos) * m.scattering;
    vec3 integrated = (source - source * stepTransmittance) / max(m.extinction, vec3(1e-12));

    result.luminance += result.transmittance * integrated;
    result.transmittance *= stepTransmittance;

}

// In-scattered light along a ray, per unit of sun illuminance, with multiple scattering from the LUT.
ScatteringResult IntegrateScattering(vec3 origin, vec3 direction, vec3 sunDirection, int samples, float maxDistance) {

    ScatteringResult result;

    result.luminance = vec3(0.0);
    result.transmittance = vec3(1.0);

    float topNear;
    float topFar;

    if (!RaySphere(origin, direction, TopRadius(), topNear, topFar) || topFar < 0.0) {

        return result;

    }

    float start = max(topNear, 0.0);
    float end = topFar;
    float groundNear;
    float groundFar;

    if (RaySphere(origin, direction, BottomRadius(), groundNear, groundFar) && groundNear > 0.0) {

        end = min(end, groundNear);

    }

    end = min(end, maxDistance);

    if (end <= start) {

        return result;

    }

    float cosTheta = dot(direction, sunDirection);
    float rayleighPhase = RayleighPhase(cosTheta);
    float miePhase = MiePhase(cosTheta);
    float dt = (end - start) / float(samples);

    for (int i = 0; i < samples; i++) {

        ScatterStep(origin + direction * (start + (float(i) + 0.3) * dt), dt, sunDirection, rayleighPhase, miePhase, result);

    }

    return result;

}

// Hillaire's sky-view mapping: latitude packed towards the horizon, azimuth relative to the sun.
vec2 SkyViewUv(bool groundHit, float viewZenithCos, float lightViewCos, float viewHeight) {

    float horizon = sqrt(max(viewHeight * viewHeight - BottomRadius() * BottomRadius(), 0.0));
    float beta = acos(clamp(horizon / viewHeight, -1.0, 1.0));
    float zenithHorizonAngle = PI - beta;
    float zenith = acos(clamp(viewZenithCos, -1.0, 1.0));

    vec2 uv;

    if (!groundHit) {

        float coord = 1.0 - sqrt(max(1.0 - zenith / zenithHorizonAngle, 0.0));

        uv.y = coord * 0.5;

    } else {

        float coord = sqrt(max((zenith - zenithHorizonAngle) / beta, 0.0));

        uv.y = coord * 0.5 + 0.5;

    }

    uv.x = sqrt(clamp(-lightViewCos * 0.5 + 0.5, 0.0, 1.0));

    return vec2(UnitToSubUv(uv.x, SkyViewLutSize.x), UnitToSubUv(uv.y, SkyViewLutSize.y));

}

void SkyViewParams(vec2 uv, float viewHeight, out float viewZenithCos, out float lightViewCos) {

    float horizon = sqrt(max(viewHeight * viewHeight - BottomRadius() * BottomRadius(), 0.0));
    float beta = acos(clamp(horizon / viewHeight, -1.0, 1.0));
    float zenithHorizonAngle = PI - beta;
    float zenith;

    if (uv.y < 0.5) {

        float coord = 1.0 - uv.y * 2.0;

        zenith = zenithHorizonAngle * (1.0 - coord * coord);

    } else {

        float coord = uv.y * 2.0 - 1.0;

        zenith = zenithHorizonAngle + beta * coord * coord;

    }

    viewZenithCos = cos(zenith);
    lightViewCos = -(uv.x * uv.x * 2.0 - 1.0);

}

// Sky luminance per unit sun illuminance from the sky-view LUT, for a camera inside the atmosphere.
vec3 SkyViewLuminance(vec3 origin, vec3 direction, vec3 sunDirection) {

    float viewHeight = length(origin);
    vec3 up = origin / viewHeight;
    float viewZenithCos = dot(direction, up);

    vec3 sunFlat = sunDirection - up * dot(sunDirection, up);
    vec3 viewFlat = direction - up * viewZenithCos;
    float lightViewCos = dot(normalize(sunFlat + vec3(1e-7)), normalize(viewFlat + vec3(1e-7)));

    return texture(skyViewLut, SkyViewUv(HitsGround(origin, direction), viewZenithCos, lightViewCos, viewHeight)).rgb;

}

// Sunlight reaching a point through the atmosphere, in lux.
vec3 SunIlluminanceAt(vec3 worldPosition) {

    if (!AtmosphereEnabled()) {

        return frame.sunIlluminance.rgb;

    }

    vec3 p = PlanetRelative(worldPosition);
    float r = length(p);
    float sunCos = dot(p / r, frame.sunDirection.xyz);

    return frame.sunIlluminance.rgb * Transmittance(r, sunCos) * SunVisibility(r, sunCos);

}

// Sky light above and light reflected by the ground below, blended by how much the normal faces up.
vec3 AtmosphereIrradiance(vec3 worldPosition, vec3 n) {

    vec3 p = PlanetRelative(worldPosition);
    float r = length(p);
    vec3 up = p / r;
    float sunCos = dot(up, frame.sunDirection.xyz);
    vec3 sun = frame.sunIlluminance.rgb;

    vec3 sky = texture(irradianceLut, IrradianceUv(r, sunCos)).rgb * sun;
    vec3 groundSky = texture(irradianceLut, IrradianceUv(BottomRadius(), sunCos)).rgb * sun;
    vec3 groundSun = Transmittance(BottomRadius(), sunCos) * SunVisibility(BottomRadius(), sunCos) * max(sunCos, 0.0) * sun;

    float view = BottomRadius() / max(r, BottomRadius());
    vec3 below = (groundSun + groundSky) * frame.groundAlbedo.rgb * view * view;
    float t = dot(n, up) * 0.5 + 0.5;

    return sky * t + below * (1.0 - t);

}

// How much the atmosphere model, rather than the camera's hemisphere, should light a point.
float AtmosphereWeight(vec3 worldPosition) {

    if (!AtmosphereEnabled()) {

        return 0.0;

    }

    return 1.0 - smoothstep(3.0, 6.0, length(PlanetRelative(worldPosition)) / TopRadius());

}

// Light scattered in front of a surface from high above, marched per pixel with steps crowding towards the dense air below.
ScatteringResult MarchToSurface(vec3 origin, vec3 direction, float distance) {

    const int Samples = 24;

    ScatteringResult result;

    result.luminance = vec3(0.0);
    result.transmittance = vec3(1.0);

    float near;
    float far;

    if (!RaySphere(origin, direction, TopRadius(), near, far) || far < 0.0) {

        return result;

    }

    float start = max(near, 0.0);
    float span = min(distance, far) - start;

    if (span <= 0.0) {

        return result;

    }

    float cosTheta = dot(direction, frame.sunDirection.xyz);
    float rayleighPhase = RayleighPhase(cosTheta);
    float miePhase = MiePhase(cosTheta);
    float previous = 0.0;

    for (int i = 1; i <= Samples; i++) {

        float u = 1.0 - float(Samples - i) / float(Samples);
        float t = 1.0 - (1.0 - u) * (1.0 - u) * (1.0 - u);
        float dt = (t - previous) * span;

        ScatterStep(origin + direction * (start + (previous + t) * 0.5 * span), dt, frame.sunDirection.xyz, rayleighPhase, miePhase, result);

        previous = t;

    }

    return result;

}

vec3 FroxelAerialPerspective(vec3 radiance, float distance, vec2 fragCoord) {

    float maxDistance = frame.aerialPerspective.x;
    float slices = frame.aerialPerspective.y;

    // Slice k ends at maxDistance * ((k + 1) / slices)^2, which puts its texel centre here.
    float depth = sqrt(distance / maxDistance) - 0.5 / slices;
    vec4 ap = texture(aerialPerspective, vec3(fragCoord * frame.viewport.zw, max(depth, 0.5 / slices)));

    float first = maxDistance / (slices * slices);
    float fade = clamp(distance / first, 0.0, 1.0);

    return radiance * mix(1.0, ap.a, fade) + ap.rgb * fade * frame.sunIlluminance.rgb;

}

// Aerial perspective between the camera and a surface; radiance is before exposure. aerialPerspective.w picks the method.
vec3 ApplyAerialPerspective(vec3 radiance, vec3 worldPosition, vec2 fragCoord) {

    if (!AtmosphereEnabled() || frame.aerialPerspective.z < 0.5) {

        return radiance;

    }

    vec3 offset = worldPosition - frame.cameraPosition.xyz;
    float distance = length(offset);
    float marched = frame.aerialPerspective.w;

    vec3 froxel = marched < 1.0 ? FroxelAerialPerspective(radiance, distance, fragCoord) : radiance;

    if (marched <= 0.0) {

        return froxel;

    }

    ScatteringResult r = MarchToSurface(PlanetRelative(frame.cameraPosition.xyz), offset / distance, distance);
    vec3 direct = radiance * r.transmittance + r.luminance * frame.sunIlluminance.rgb;

    return mix(froxel, direct, marched);

}

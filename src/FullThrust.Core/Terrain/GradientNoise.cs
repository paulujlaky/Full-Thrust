namespace FullThrust.Core.Terrain;

// Improved Perlin noise in double precision; seeded and allocation-free after construction.
public sealed class GradientNoise {

    private readonly int[] _perm = new int[512];

    public GradientNoise(int seed) {

        var values = new int[256];

        for (var i = 0; i < 256; i++) {

            values[i] = i;

        }

        var state = (uint)seed * 2654435761u + 1013904223u;

        for (var i = 255; i > 0; i--) {

            state = state * 1664525u + 1013904223u;

            var j = (int)(state >> 8) % (i + 1);

            (values[i], values[j]) = (values[j], values[i]);

        }

        for (var i = 0; i < 512; i++) {

            _perm[i] = values[i & 255];

        }

    }

    // Roughly in [-1, 1].
    public double Noise(double x, double y, double z) {

        var fx = Math.Floor(x);
        var fy = Math.Floor(y);
        var fz = Math.Floor(z);

        var xi = (int)((long)fx & 255);
        var yi = (int)((long)fy & 255);
        var zi = (int)((long)fz & 255);

        x -= fx;
        y -= fy;
        z -= fz;

        var u = Fade(x);
        var v = Fade(y);
        var w = Fade(z);

        var p = _perm;

        var a = p[xi] + yi;
        var aa = p[a] + zi;
        var ab = p[a + 1] + zi;
        var b = p[xi + 1] + yi;
        var ba = p[b] + zi;
        var bb = p[b + 1] + zi;

        return Lerp(w,
            Lerp(v, Lerp(u, Grad(p[aa], x, y, z), Grad(p[ba], x - 1, y, z)), Lerp(u, Grad(p[ab], x, y - 1, z), Grad(p[bb], x - 1, y - 1, z))),
            Lerp(v, Lerp(u, Grad(p[aa + 1], x, y, z - 1), Grad(p[ba + 1], x - 1, y, z - 1)), Lerp(u, Grad(p[ab + 1], x, y - 1, z - 1), Grad(p[bb + 1], x - 1, y - 1, z - 1))));

    }

    // Fractal sum normalised to roughly [-1, 1].
    public double Fbm(double x, double y, double z, int octaves, double lacunarity = 2.0, double gain = 0.5) {

        var sum = 0.0;
        var amplitude = 1.0;
        var total = 0.0;

        for (var i = 0; i < octaves; i++) {

            sum += amplitude * Noise(x, y, z);
            total += amplitude;
            amplitude *= gain;
            x = x * lacunarity + 17.31;
            y = y * lacunarity + 5.77;
            z = z * lacunarity + 29.13;

        }

        return sum / total;

    }

    // Sharp crests where the noise crosses zero; in [0, 1].
    public double Ridged(double x, double y, double z, int octaves) {

        var sum = 0.0;
        var amplitude = 0.5;
        var weight = 1.0;
        var total = 0.0;

        for (var i = 0; i < octaves; i++) {

            var n = 1 - Math.Abs(Noise(x, y, z));

            n *= n * weight;
            weight = Math.Clamp(n * 2, 0, 1);

            sum += n * amplitude;
            total += amplitude;
            amplitude *= 0.5;
            x = x * 2 + 11.7;
            y = y * 2 + 3.1;
            z = z * 2 + 7.9;

        }

        return sum / total;

    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private static double Grad(int hash, double x, double y, double z) {

        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h is 12 or 14 ? x : z;

        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);

    }

}

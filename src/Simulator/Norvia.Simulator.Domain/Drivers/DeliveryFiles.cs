namespace Norvia.Simulator.Domain.Drivers;

/// <summary>
/// The files a simulated Driver attaches to a Proof of delivery: a drawn signature and, for a delivery with reservations, a placeholder photo. Both are PNGs
/// of a few KB, drawn from the run's seed and the Shipment number alone, so the same run gives the same files and no two Shipments get the same one. The
/// photo is a generated picture, never a real one.
/// </summary>
public static class DeliveryFiles
{
    private const int SignatureWidth = 600;
    private const int SignatureHeight = 200;
    private const int PhotoWidth = 320;
    private const int PhotoHeight = 240;

    /// <summary>A handwritten-looking stroke in dark ink on a transparent background: a flourish of connected loops and an underline.</summary>
    public static byte[] Signature(int seed, string shipmentNumber)
    {
        var random = new SeededRandom(Mix(seed, shipmentNumber));
        var alpha = new byte[SignatureWidth * SignatureHeight];

        // The name: a run of loops that drifts to the right, as a signature does.
        var x = random.Between(30, 80);
        var baseline = random.Between(100, 130);
        var points = new List<(double X, double Y)> { (x, baseline) };
        var letters = random.Between(5, 9);
        for (var i = 0; i < letters; i++)
        {
            var width = random.Between(30, 60);
            var height = random.Between(30, 85);
            points.Add((x + (width * 0.3), baseline - height));
            points.Add((x + (width * 0.6), baseline + random.Between(0, 30)));
            points.Add((x + width, baseline - random.Between(0, 15)));
            x += width;
        }

        Stroke(alpha, points, 2.4);

        // The underline: one quick stroke below the name.
        var left = random.Between(20, 100);
        var level = Math.Min(SignatureHeight - 15, baseline + random.Between(35, 55));
        Stroke(alpha, [(left, level), ((left + x) / 2.0, level + random.Between(-8, 8)), (x + random.Between(0, 20), level - random.Between(0, 12))], 1.8);

        var pixels = new byte[alpha.Length * 2];
        for (var i = 0; i < alpha.Length; i++)
        {
            pixels[i * 2] = 20;
            pixels[(i * 2) + 1] = alpha[i];
        }

        return PngWriter.Encode(SignatureWidth, SignatureHeight, colorType: 4, channels: 2, pixels);
    }

    /// <summary>A generated placeholder photo: a floor, a wall and a few cartons, one with a dent.</summary>
    public static byte[] Photo(int seed, string shipmentNumber)
    {
        var random = new SeededRandom(Mix(seed, shipmentNumber) ^ 0x5F3759DF);
        var pixels = new byte[PhotoWidth * PhotoHeight * 3];
        var horizon = random.Between(100, 140);
        var wall = (R: random.Between(150, 200), G: random.Between(150, 200), B: random.Between(150, 200));
        for (var y = 0; y < PhotoHeight; y++)
        {
            var shade = y < horizon ? 1.0 : 0.55 + (0.3 * (y - horizon) / (PhotoHeight - horizon));
            for (var x = 0; x < PhotoWidth; x++)
            {
                Set(pixels, x, y, (int)(wall.R * shade), (int)(wall.G * shade), (int)(wall.B * shade));
            }
        }

        var cartons = random.Between(2, 4);
        var left = random.Between(15, 60);
        for (var i = 0; i < cartons; i++)
        {
            var width = random.Between(60, 90);
            var height = random.Between(50, 100);
            var top = horizon + 60 - height;
            var (r, g, b) = (random.Between(150, 200), random.Between(110, 150), random.Between(60, 90));
            Fill(pixels, left, top, width, height, r, g, b);
            Fill(pixels, left, top + (height / 2), width, 3, r - 50, g - 40, b - 30);
            if (i == cartons - 1)
            {
                Fill(pixels, left + width - 18, top, 18, 14, r - 70, g - 60, b - 40);
            }

            left += width + random.Between(5, 20);
        }

        return PngWriter.Encode(PhotoWidth, PhotoHeight, colorType: 2, channels: 3, pixels);
    }

    // A stable mix of the seed and the Shipment number: string.GetHashCode differs per process, so FNV-1a.
    private static int Mix(int seed, string shipmentNumber)
    {
        ArgumentNullException.ThrowIfNull(shipmentNumber);
        var hash = 2166136261u;
        foreach (var c in shipmentNumber)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return unchecked((int)hash ^ seed);
    }

    // A smooth curve through the points (Catmull-Rom), inked with a round pen.
    private static void Stroke(byte[] alpha, List<(double X, double Y)> points, double radius)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Math.Max(i - 1, 0)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(i + 2, points.Count - 1)];
            const int steps = 40;
            for (var s = 0; s <= steps; s++)
            {
                var t = s / (double)steps;
                Ink(alpha, Spline(p0.X, p1.X, p2.X, p3.X, t), Spline(p0.Y, p1.Y, p2.Y, p3.Y, t), radius);
            }
        }
    }

    private static double Spline(double a, double b, double c, double d, double t) =>
        0.5 * ((2 * b) + ((c - a) * t) + (((2 * a) - (5 * b) + (4 * c) - d) * t * t) + (((3 * b) - a - (3 * c) + d) * t * t * t));

    private static void Ink(byte[] alpha, double cx, double cy, double radius)
    {
        for (var y = (int)Math.Floor(cy - radius - 1); y <= (int)Math.Ceiling(cy + radius + 1); y++)
        {
            for (var x = (int)Math.Floor(cx - radius - 1); x <= (int)Math.Ceiling(cx + radius + 1); x++)
            {
                if (x < 0 || y < 0 || x >= SignatureWidth || y >= SignatureHeight)
                {
                    continue;
                }

                var distance = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
                var coverage = (byte)(255 * Math.Clamp(radius + 0.5 - distance, 0, 1));
                alpha[(y * SignatureWidth) + x] = Math.Max(alpha[(y * SignatureWidth) + x], coverage);
            }
        }
    }

    private static void Fill(byte[] pixels, int left, int top, int width, int height, int r, int g, int b)
    {
        for (var y = Math.Max(top, 0); y < Math.Min(top + height, PhotoHeight); y++)
        {
            for (var x = Math.Max(left, 0); x < Math.Min(left + width, PhotoWidth); x++)
            {
                Set(pixels, x, y, r, g, b);
            }
        }
    }

    private static void Set(byte[] pixels, int x, int y, int r, int g, int b)
    {
        var at = ((y * PhotoWidth) + x) * 3;
        pixels[at] = (byte)Math.Clamp(r, 0, 255);
        pixels[at + 1] = (byte)Math.Clamp(g, 0, 255);
        pixels[at + 2] = (byte)Math.Clamp(b, 0, 255);
    }
}

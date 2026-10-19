namespace Tangerine.Core.Wheel;

public readonly record struct Ring(double InnerRadius, double OuterRadius, int Count)
{
    public double Sweep => Count == 0 ? 0 : 360.0 / Count;

    /// <summary>Angle (degrees, clockwise from 12 o'clock) where segment <paramref name="index"/> starts. Segment 0 is centred at the top.</summary>
    public double StartAngle(int index) => index * Sweep - Sweep / 2;

    public double MidAngle(int index) => index * Sweep;

    public double MidRadius => (InnerRadius + OuterRadius) / 2;
}

public readonly record struct WheelHit(int Ring, int Index)
{
    public static readonly WheelHit None = new(-1, -1);
    public bool IsNone => Ring < 0;
}

/// <summary>Geometry of the radial wheel: concentric rings around a central "cancel" hole.</summary>
public sealed class RadialLayout
{
    public const double DefaultHoleRadius = 54;
    public const double RingGap = 6;

    public RadialLayout(IReadOnlyList<int> ringCounts, double holeRadius = DefaultHoleRadius, double ringThickness = 84, double outerRingThickness = 74)
    {
        HoleRadius = holeRadius;
        var rings = new List<Ring>();
        var inner = holeRadius;
        foreach (var count in ringCounts.Where(c => c > 0))
        {
            var thickness = rings.Count == 0 ? ringThickness : outerRingThickness;
            rings.Add(new Ring(inner, inner + thickness, count));
            inner += thickness + RingGap;
        }
        Rings = rings;
    }

    public double HoleRadius { get; }
    public IReadOnlyList<Ring> Rings { get; }
    public double OuterRadius => Rings.Count == 0 ? HoleRadius : Rings[^1].OuterRadius;

    /// <summary>Angle in degrees, clockwise from 12 o'clock, in [0, 360).</summary>
    public static double AngleOf(double dx, double dy)
    {
        var degrees = Math.Atan2(dx, -dy) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    public static (double X, double Y) PointAt(double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return (radius * Math.Sin(radians), -radius * Math.Cos(radians));
    }

    /// <summary>Finds the segment under an offset from the wheel centre. Ring gaps count as the ring outside them.</summary>
    public WheelHit HitTest(double dx, double dy)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < HoleRadius || Rings.Count == 0) return WheelHit.None;

        var angle = AngleOf(dx, dy);
        for (var r = 0; r < Rings.Count; r++)
        {
            var isLast = r == Rings.Count - 1;
            if (distance <= Rings[r].OuterRadius) return new WheelHit(r, IndexAt(Rings[r], angle));
            if (!isLast && distance < Rings[r + 1].InnerRadius) return new WheelHit(r + 1, IndexAt(Rings[r + 1], angle));
            // Overshooting the outermost ring still selects, so fast flicks land on a segment.
            if (isLast && distance <= Rings[r].OuterRadius + OvershootTolerance) return new WheelHit(r, IndexAt(Rings[r], angle));
        }
        return WheelHit.None;
    }

    public const double OvershootTolerance = 60;

    private static int IndexAt(Ring ring, double angle) =>
        (int)Math.Floor((angle + ring.Sweep / 2) / ring.Sweep) % ring.Count;
}

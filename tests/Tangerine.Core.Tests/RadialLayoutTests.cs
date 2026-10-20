using Tangerine.Core.Wheel;

namespace Tangerine.Core.Tests;

public class RadialLayoutTests
{
    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1, 0, 90)]
    [InlineData(0, 1, 180)]
    [InlineData(-1, 0, 270)]
    public void Angles_are_clockwise_from_top(double dx, double dy, double expected) =>
        Assert.Equal(expected, RadialLayout.AngleOf(dx, dy), 6);

    [Fact]
    public void Centre_is_cancel_zone()
    {
        var layout = new RadialLayout([8, 4]);
        Assert.True(layout.HitTest(0, 0).IsNone);
        Assert.True(layout.HitTest(10, -20).IsNone);
    }

    [Fact]
    public void Segments_are_centred_on_their_angle()
    {
        var layout = new RadialLayout([4]);
        var r = layout.Rings[0].MidRadius;
        Assert.Equal(new WheelHit(0, 0), layout.HitTest(0, -r));
        Assert.Equal(new WheelHit(0, 1), layout.HitTest(r, 0));
        Assert.Equal(new WheelHit(0, 2), layout.HitTest(0, r));
        Assert.Equal(new WheelHit(0, 3), layout.HitTest(-r, 0));
        Assert.Equal(new WheelHit(0, 0), layout.HitTest(-r * 0.3, -r));
    }

    [Fact]
    public void Outer_ring_and_overshoot_hit_tools()
    {
        var layout = new RadialLayout([8, 3]);
        var outer = layout.Rings[1];
        Assert.Equal(new WheelHit(1, 0), layout.HitTest(0, -outer.MidRadius));
        Assert.Equal(new WheelHit(1, 0), layout.HitTest(0, -(outer.OuterRadius + 30)));
        Assert.True(layout.HitTest(0, -(outer.OuterRadius + 100)).IsNone);
        Assert.Equal(new WheelHit(1, 0), layout.HitTest(0, -(layout.Rings[0].OuterRadius + RadialLayout.RingGap / 2)));
    }

    [Fact]
    public void Empty_rings_are_skipped()
    {
        var layout = new RadialLayout([0, 5]);
        Assert.Single(layout.Rings);
        Assert.Equal(5, layout.Rings[0].Count);
    }

    [Fact]
    public void Single_segment_covers_everything()
    {
        var layout = new RadialLayout([1]);
        var r = layout.Rings[0].MidRadius;
        Assert.Equal(new WheelHit(0, 0), layout.HitTest(0, r));
        Assert.Equal(new WheelHit(0, 0), layout.HitTest(-r, 0));
    }
}

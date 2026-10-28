using KelvinTwo;
using Xunit;

namespace KelvinTwo.Tests;

public sealed class WarpDeciderTests
{
    private static WarpDecider Decider() => new(minDistance: 60f, cooldownSeconds: 60f, headingWindowSeconds: 3f, minClosingMeters: 2f);

    [Fact]
    public void Warps_WhenCarryingFarAndHeadingBack()
    {
        var d = Decider();

        Assert.False(d.Update(true, 200f, 0f));  // first sample
        Assert.False(d.Update(true, 198f, 1f));  // still inside the heading window
        Assert.True(d.Update(true, 195f, 3f));   // 5 m closer after 3 s
    }

    [Fact]
    public void NeverWarps_WhenNotCarrying()
    {
        var d = Decider();

        for (int t = 0; t <= 10; t++)
            Assert.False(d.Update(false, 200f - t * 3f, t));
    }

    [Fact]
    public void NeverWarps_WhenDropOffIsClose()
    {
        var d = Decider();

        for (int t = 0; t <= 10; t++)
            Assert.False(d.Update(true, 59f - t, t));
    }

    [Fact]
    public void DoesNotWarp_WhileStillGoingOutToCollect()
    {
        var d = Decider();

        Assert.False(d.Update(true, 100f, 0f));
        Assert.False(d.Update(true, 110f, 3f)); // walking away from the drop-off
        Assert.False(d.Update(true, 110.5f, 6f)); // standing / picking up
    }

    [Fact]
    public void RespectsCooldown_BetweenWarps()
    {
        var d = Decider();
        d.Update(true, 200f, 0f);
        Assert.True(d.Update(true, 190f, 3f));

        // Heading back again right away: blocked until 60 s after the last warp.
        Assert.False(d.Update(true, 200f, 10f));
        Assert.False(d.Update(true, 190f, 13f));
        Assert.False(d.Update(true, 200f, 62f)); // 59 s since the warp: still cooling down
        Assert.False(d.Update(true, 200f, 63f)); // cooldown over: first heading sample
        Assert.True(d.Update(true, 190f, 66f));
    }

    [Fact]
    public void StartsOver_AfterBeingInterrupted()
    {
        var d = Decider();

        Assert.False(d.Update(true, 200f, 0f));
        d.Reset();                               // e.g. a player opened the order pad
        Assert.False(d.Update(true, 190f, 3f));  // fresh sample, not a warp
        Assert.True(d.Update(true, 185f, 6f));
    }
}

using KelvinTwo;
using Xunit;

namespace KelvinTwo.Tests;

public sealed class StuckDetectorTests
{
    private static StuckDetector Detector() => new(windowSeconds: 15f, moveToleranceMeters: 1.5f, nearDropOffMeters: 4f);

    [Fact]
    public void Fires_WhenStandingStillWithLoadFarFromDropOff()
    {
        var d = Detector();

        for (int t = 0; t < 15; t++)
            Assert.False(d.Update(true, 40f, 10f, 10f, t));
        Assert.True(d.Update(true, 40f, 10.5f, 10f, 15f));   // shuffling half a meter still counts as standing
    }

    [Fact]
    public void FiresAgain_OnlyAfterAnotherFullWindow()
    {
        var d = Detector();
        d.Update(true, 40f, 0f, 0f, 0f);
        Assert.True(d.Update(true, 40f, 0f, 0f, 15f));

        Assert.False(d.Update(true, 40f, 0f, 0f, 20f));
        Assert.True(d.Update(true, 40f, 0f, 0f, 30f));
    }

    [Fact]
    public void NeverFires_WhileWalking()
    {
        var d = Detector();

        for (int t = 0; t <= 60; t++)
            Assert.False(d.Update(true, 100f - t, t * 2f, 0f, t));
    }

    [Fact]
    public void NeverFires_WhenNotCarrying()
    {
        var d = Detector();

        for (int t = 0; t <= 60; t++)
            Assert.False(d.Update(false, 40f, 0f, 0f, t));
    }

    [Fact]
    public void NeverFires_AtTheDropOff()
    {
        var d = Detector();

        for (int t = 0; t <= 60; t++)
            Assert.False(d.Update(true, 2f, 0f, 0f, t));
    }

    [Fact]
    public void StartsOver_AfterPuttingTheLoadDown()
    {
        var d = Detector();
        for (int t = 0; t < 14; t++) d.Update(true, 40f, 0f, 0f, t);

        d.Update(false, 40f, 0f, 0f, 14f);
        Assert.False(d.Update(true, 40f, 0f, 0f, 15f));
        Assert.False(d.Update(true, 40f, 0f, 0f, 29f));
        Assert.True(d.Update(true, 40f, 0f, 0f, 30f));
    }
}

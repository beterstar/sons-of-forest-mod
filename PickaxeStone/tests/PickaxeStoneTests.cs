using System;
using System.IO;
using PickaxeStone;
using Xunit;

namespace PickaxeStone.Tests;

public sealed class HitCounterTests
{
    [Fact]
    public void DropsOnEveryHit_WhenOneHitPerDrop()
    {
        var c = new HitCounter(1, 0.4f);

        Assert.True(c.RegisterHit(1, 0, 0f));
        Assert.True(c.RegisterHit(1, 1, 1f));
    }

    [Fact]
    public void CountsOncePerSwing_EvenWhenSeveralCallbacksFire()
    {
        var c = new HitCounter(1, 0f);

        Assert.True(c.RegisterHit(1, 5, 0f));
        Assert.False(c.RegisterHit(1, 5, 0.01f)); // audio + effect + impact of the same swing
        Assert.False(c.RegisterHit(2, 5, 0.02f)); // same swing touching a second rock
    }

    [Fact]
    public void IgnoresHitsCloserThanMinInterval_WhenSwingIdUnknown()
    {
        var c = new HitCounter(1, 0.4f);

        Assert.True(c.RegisterHit(1, -1, 0f));
        Assert.False(c.RegisterHit(1, -1, 0.1f));
        Assert.True(c.RegisterHit(1, -1, 0.5f));
    }

    [Fact]
    public void DropsOnlyEveryNthHit_PerRock()
    {
        var c = new HitCounter(3, 0f);

        Assert.False(c.RegisterHit(1, 0, 0f));
        Assert.False(c.RegisterHit(1, 1, 1f));
        Assert.True(c.RegisterHit(1, 2, 2f));
        Assert.False(c.RegisterHit(1, 3, 3f)); // counter restarted
    }

    [Fact]
    public void KeepsSeparateCounts_ForDifferentRocks()
    {
        var c = new HitCounter(2, 0f);

        Assert.False(c.RegisterHit(1, 0, 0f));
        Assert.False(c.RegisterHit(2, 1, 1f));
        Assert.True(c.RegisterHit(1, 2, 2f));
        Assert.True(c.RegisterHit(2, 3, 3f));
    }

    [Fact]
    public void TreatsZeroHitsPerDropAsOne()
    {
        var c = new HitCounter(0, 0f);

        Assert.True(c.RegisterHit(1, 0, 0f));
    }
}

public sealed class ConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PickaxeStoneTests_" + Guid.NewGuid().ToString("N"));

    public ConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ReadsShippedConfigWithThaiComments()
    {
        var cfg = ModConfig.Load(Path.Combine(AppContext.BaseDirectory, "config.json"));

        Assert.True(cfg.Enabled);
        Assert.Equal(1, cfg.HitsPerDrop);
        Assert.Equal(1, cfg.StonesPerDrop);
        Assert.Equal(0, cfg.DropItemId);
    }

    [Fact]
    public void ReturnsDefaults_WhenFileMissing()
    {
        var cfg = ModConfig.Load(Path.Combine(_dir, "nope.json"));

        Assert.True(cfg.Enabled);
        Assert.Equal(0.4f, cfg.MinSecondsBetweenHits);
    }

    [Fact]
    public void ClampsOutOfRangeValues()
    {
        var path = Path.Combine(_dir, "c.json");
        File.WriteAllText(path, "{ \"hitsPerDrop\": 0, \"stonesPerDrop\": 99, \"minSecondsBetweenHits\": 0, \"dropItemId\": -5 }");

        var cfg = ModConfig.Load(path);

        Assert.Equal(1, cfg.HitsPerDrop);
        Assert.Equal(5, cfg.StonesPerDrop);
        Assert.Equal(0.1f, cfg.MinSecondsBetweenHits);
        Assert.Equal(0, cfg.DropItemId);
    }
}

using System;
using System.IO;
using KelvinTwo;
using Xunit;

namespace KelvinTwo.Tests;

public sealed class ConfigAndStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "KelvinTwoTests_" + Guid.NewGuid().ToString("N"));

    public ConfigAndStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileMissing()
    {
        var cfg = ModConfig.Load(Path.Combine(_dir, "nope.json"));

        Assert.True(cfg.CloneEnabled);
        Assert.True(cfg.GliderAutoPickup);
        Assert.Equal("Kelvin 2", cfg.CloneName);
    }

    [Fact]
    public void Load_ReadsShippedConfigWithThaiComments()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "config.json");

        var cfg = ModConfig.Load(shipped);

        Assert.Equal(1.0f, cfg.SpeedMultiplier);
        Assert.True(cfg.WarpHome);
        Assert.Equal(60f, cfg.WarpMinDistance);
        Assert.Equal(10f, cfg.GliderSearchRadius);
        Assert.Equal(new[] { 0.35f, 0.65f, 1.0f }, cfg.TintRgb);
        Assert.True(cfg.AlwaysObedient);
        Assert.Equal(5f, cfg.SearchMultiplier);
    }

    [Fact]
    public void Load_ClampsSearchMultiplier()
    {
        Assert.Equal(1f, ModConfig.Load(Write("lo.json", "{ \"searchMultiplier\": 0.2 }")).SearchMultiplier);
        Assert.Equal(10f, ModConfig.Load(Write("hi.json", "{ \"searchMultiplier\": 50 }")).SearchMultiplier);
    }

    [Fact]
    public void Load_CanTurnOffAlwaysObedient()
    {
        var path = Write("c.json", "{ \"alwaysObedient\": false }");

        var cfg = ModConfig.Load(path);

        Assert.False(cfg.AlwaysObedient);
    }

    [Fact]
    public void Load_ClampsOutOfRangeValues()
    {
        var path = Write("c.json", "{ \"speedMultiplier\": 99, \"visionMultiplier\": 0, \"sentiment\": -500, }");

        var cfg = ModConfig.Load(path);

        Assert.Equal(3f, cfg.SpeedMultiplier);
        Assert.Equal(1f, cfg.VisionMultiplier);
        Assert.Equal(-100f, cfg.Sentiment);
    }

    [Fact]
    public void Load_KeepsDefaultTint_WhenTintIsMalformed()
    {
        var path = Write("c.json", "{ \"tintRgb\": [1, 0] }");

        var cfg = ModConfig.Load(path);

        Assert.Equal(new[] { 0.35f, 0.65f, 1.0f }, cfg.TintRgb);
    }

    [Fact]
    public void CloneStore_PersistsCloneIdPerSave()
    {
        var path = Path.Combine(_dir, "clones.json");

        new CloneStore(path).Set("1215123665", 42);
        var reloaded = new CloneStore(path);

        Assert.True(reloaded.TryGet("1215123665", out var id));
        Assert.Equal(42, id);
        Assert.False(reloaded.TryGet("other", out _));
    }
}

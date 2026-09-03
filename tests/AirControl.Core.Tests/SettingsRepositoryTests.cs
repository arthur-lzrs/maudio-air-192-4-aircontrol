using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

public class SettingsRepositoryTests : IDisposable
{
    private readonly string _tempFilePath;

    public SettingsRepositoryTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"air-control-tests-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileMissing()
    {
        var repository = new SettingsRepository(_tempFilePath);

        var profile = repository.Load();

        Assert.Equal(ChannelSettingsProfile.Default, profile);
        Assert.Equal(0, profile.Input1.TrimDb);
        Assert.False(profile.Input1.IsMuted);
        Assert.False(profile.Input1.IsSoloed);
        Assert.Null(profile.OutputDeviceId);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileCorrupted()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tempFilePath)!);
        File.WriteAllText(_tempFilePath, "{ this is not valid json ");
        var repository = new SettingsRepository(_tempFilePath);

        var profile = repository.Load();

        Assert.Equal(ChannelSettingsProfile.Default, profile);
    }

    [Fact]
    public void Save_ThenLoad_ReturnsIdenticalProfile()
    {
        var repository = new SettingsRepository(_tempFilePath);
        var original = new ChannelSettingsProfile(
            Input1: new ChannelSettings(TrimDb: 6.5, IsMuted: true, IsSoloed: false),
            Input2: new ChannelSettings(TrimDb: -3.0, IsMuted: false, IsSoloed: true),
            OutputDeviceId: "device-123");

        repository.Save(original);
        var loaded = repository.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsNegativeInfinityTrim()
    {
        var repository = new SettingsRepository(_tempFilePath);
        var original = new ChannelSettingsProfile(
            Input1: new ChannelSettings(TrimDb: double.NegativeInfinity, IsMuted: false, IsSoloed: false),
            Input2: ChannelSettings.Default,
            OutputDeviceId: null);

        repository.Save(original);
        var loaded = repository.Load();

        Assert.Equal(double.NegativeInfinity, loaded.Input1.TrimDb);
    }
}

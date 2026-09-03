using AirControl.Core;
using Xunit;

namespace AirControl.Integration.Tests;

public class RoutingPersistenceIntegrationTests : IDisposable
{
    private readonly string _tempFilePath;

    public RoutingPersistenceIntegrationTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"air-control-routing-tests-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    [Theory]
    [InlineData(RoutingMode.Input1Mono)]
    [InlineData(RoutingMode.Input2Mono)]
    [InlineData(RoutingMode.Stereo)]
    [InlineData(RoutingMode.CombinedMono)]
    public void SavedRoutingMode_IsRestoredCorrectly_OnReload(RoutingMode mode)
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        var profile = repository.Load();

        var updated = profile with { RoutingMode = mode };
        repository.Save(updated);

        var reloaded = repository.Load();

        Assert.Equal(mode, reloaded.RoutingMode);
    }

    [Fact]
    public void SavedInputDeviceId_IsRestoredCorrectly_OnReload()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        var profile = repository.Load();

        var updated = profile with { InputDeviceId = "some-device-id" };
        repository.Save(updated);

        var reloaded = repository.Load();

        Assert.Equal("some-device-id", reloaded.InputDeviceId);
    }

    [Fact]
    public void LoadingFile_WithoutRoutingFields_DefaultsToStereoAndNullInputDevice()
    {
        var legacyJson = "{\"Input1\":{\"TrimDb\":0,\"IsMuted\":false,\"IsSoloed\":false}," +
                          "\"Input2\":{\"TrimDb\":0,\"IsMuted\":false,\"IsSoloed\":false}," +
                          "\"OutputDeviceId\":\"legacy-output\"}";
        File.WriteAllText(_tempFilePath, legacyJson);

        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        var loaded = repository.Load();

        Assert.Equal(RoutingMode.Stereo, loaded.RoutingMode);
        Assert.Null(loaded.InputDeviceId);
        Assert.Equal("legacy-output", loaded.OutputDeviceId);
    }
}

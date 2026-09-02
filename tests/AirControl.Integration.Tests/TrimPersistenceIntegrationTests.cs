using AirControl.Core;
using Xunit;

namespace AirControl.Integration.Tests;

public class TrimPersistenceIntegrationTests : IDisposable
{
    private readonly string _tempFilePath;

    public TrimPersistenceIntegrationTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"air-control-trim-tests-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    [Fact]
    public void SavedTrim_IsRestoredCorrectly_OnReload()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        var profile = repository.Load();

        var updated = profile with { Input1 = profile.Input1 with { TrimDb = 8.5 } };
        repository.Save(updated);

        var reloaded = repository.Load();

        Assert.Equal(8.5, reloaded.Input1.TrimDb);
        Assert.Equal(profile.Input2.TrimDb, reloaded.Input2.TrimDb);
    }
}

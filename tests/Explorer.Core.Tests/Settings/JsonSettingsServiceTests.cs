using Explorer.Core.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Core.Tests.Settings;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFile;

    public JsonSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsFile = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private JsonSettingsService CreateService() =>
        new(_settingsFile, NullLogger<JsonSettingsService>.Instance);

    [Fact]
    public void Current_BeforeLoad_HasDefaults()
    {
        var service = CreateService();

        service.Current.Should().Be(new AppSettings());
        service.Current.SchemaVersion.Should().Be(AppSettings.CurrentSchemaVersion);
        service.Current.Theme.Should().Be(AppTheme.System);
    }

    [Fact]
    public void Load_WhenFileMissing_KeepsDefaults()
    {
        var service = CreateService();

        service.Load();

        service.Current.Should().Be(new AppSettings());
    }

    [Fact]
    public void Update_PersistsToDisk_AndReturnsNewSnapshot()
    {
        var service = CreateService();

        var updated = service.Update(s => s with { Theme = AppTheme.Dark });

        updated.Theme.Should().Be(AppTheme.Dark);
        service.Current.Theme.Should().Be(AppTheme.Dark);

        var reloaded = CreateService();
        reloaded.Load();
        reloaded.Current.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void Update_DoesNotMutatePreviousSnapshot()
    {
        var service = CreateService();
        var before = service.Current;

        service.Update(s => s with { Theme = AppTheme.Light });

        before.Theme.Should().Be(AppTheme.System);
    }

    [Fact]
    public void Load_WhenJsonCorrupted_FallsBackToDefaults_AndBacksUpFile()
    {
        File.WriteAllText(_settingsFile, "{ this is not valid json !!!");
        var service = CreateService();

        service.Load();

        service.Current.Should().Be(new AppSettings());
        File.Exists(_settingsFile + ".bak").Should().BeTrue();
    }

    [Fact]
    public void Load_AfterUpdate_RoundtripsAllProperties()
    {
        CreateService().Update(s => s with { Theme = AppTheme.Light, ShowHiddenFiles = true });

        var reloaded = CreateService();
        reloaded.Load();

        reloaded.Current.Should().Be(new AppSettings { Theme = AppTheme.Light, ShowHiddenFiles = true });
    }

    [Fact]
    public void Update_WithNullTransform_Throws()
    {
        var service = CreateService();

        var act = () => service.Update(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_WithBlankPath_Throws()
    {
        var act = () => new JsonSettingsService("  ", NullLogger<JsonSettingsService>.Instance);

        act.Should().Throw<ArgumentException>();
    }
}

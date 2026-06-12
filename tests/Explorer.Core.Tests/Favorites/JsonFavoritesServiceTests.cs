using Explorer.Core.Favorites;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Core.Tests.Favorites;

public sealed class JsonFavoritesServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _favoritesFile;

    public JsonFavoritesServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _favoritesFile = Path.Combine(_tempDir, "favorites.json");
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

    private JsonFavoritesService CreateService() =>
        new(_favoritesFile, NullLogger<JsonFavoritesService>.Instance);

    [Fact]
    public void Load_FileMissing_GivesEmptyList()
    {
        var service = CreateService();

        service.Load();

        service.Items.Should().BeEmpty();
    }

    [Fact]
    public void Add_PersistsAndRaisesChanged()
    {
        var service = CreateService();
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.Add(@"C:\Some\Folder", isDirectory: true);

        service.Items.Should().ContainSingle(i => i.Path == @"C:\Some\Folder" && i.IsDirectory);
        raised.Should().Be(1);

        var reloaded = CreateService();
        reloaded.Load();
        reloaded.Items.Should().ContainSingle(i => i.Path == @"C:\Some\Folder");
    }

    [Fact]
    public void Add_DuplicatePath_CaseInsensitive_IsIgnored()
    {
        var service = CreateService();
        service.Add(@"C:\Data", isDirectory: true);

        service.Add(@"C:\DATA\", isDirectory: true);

        service.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Add_AutoDetectsDirectory_WhenNotSpecified()
    {
        var realDir = Path.Combine(_tempDir, "realdir");
        Directory.CreateDirectory(realDir);
        var realFile = Path.Combine(_tempDir, "file.txt");
        File.WriteAllText(realFile, "x");
        var service = CreateService();

        service.Add(realDir);
        service.Add(realFile);

        service.Items[0].IsDirectory.Should().BeTrue();
        service.Items[1].IsDirectory.Should().BeFalse();
    }

    [Fact]
    public void Remove_DeletesByPathIgnoringCase()
    {
        var service = CreateService();
        service.Add(@"C:\Data", isDirectory: true);

        service.Remove(@"c:\data");

        service.Items.Should().BeEmpty();
    }

    [Fact]
    public void Move_ReordersAndClampsIndex()
    {
        var service = CreateService();
        service.Add(@"C:\a", true);
        service.Add(@"C:\b", true);
        service.Add(@"C:\c", true);

        service.Move(@"C:\c", 0);
        service.Items.Select(i => i.Path).Should().Equal(@"C:\c", @"C:\a", @"C:\b");

        service.Move(@"C:\c", 99);
        service.Items.Select(i => i.Path).Should().Equal(@"C:\a", @"C:\b", @"C:\c");
    }

    [Fact]
    public void Load_CorruptedJson_BacksUpAndGivesEmpty()
    {
        File.WriteAllText(_favoritesFile, "{ broken!!");
        var service = CreateService();

        service.Load();

        service.Items.Should().BeEmpty();
        File.Exists(_favoritesFile + ".bak").Should().BeTrue();
    }

    [Fact]
    public void Order_SurvivesRoundtrip()
    {
        var service = CreateService();
        service.Add(@"C:\b", true);
        service.Add(@"C:\a", true);
        service.Move(@"C:\a", 0);

        var reloaded = CreateService();
        reloaded.Load();

        reloaded.Items.Select(i => i.Path).Should().Equal(@"C:\a", @"C:\b");
    }
}

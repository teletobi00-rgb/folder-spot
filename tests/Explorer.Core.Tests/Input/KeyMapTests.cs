using Explorer.Core.Input;
using FluentAssertions;

namespace Explorer.Core.Tests.Input;

public sealed class KeyMapTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _keymapFile;

    public KeyMapTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _keymapFile = Path.Combine(_tempDir, "keymap.json");
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

    [Fact]
    public void Default_ContainsTotalCommanderContract()
    {
        var map = KeyMap.CreateDefault();

        map.GestureFor(KeyActions.CopyToOtherPane).Should().Be("F5");
        map.GestureFor(KeyActions.MoveToOtherPane).Should().Be("F6");
        map.GestureFor(KeyActions.SwitchPane).Should().Be("Tab");
        map.GestureFor(KeyActions.SwapPanes).Should().Be("Ctrl+U");
        map.GestureFor(KeyActions.OpenInOtherPane).Should().Contain("Ctrl+Right");
    }

    [Fact]
    public void LoadWithOverrides_FileMissing_UsesDefaults()
    {
        var map = KeyMap.LoadWithOverrides(_keymapFile);

        map.Bindings.Should().BeEquivalentTo(KeyMap.CreateDefault().Bindings);
    }

    [Fact]
    public void LoadWithOverrides_MergesKnownActions()
    {
        File.WriteAllText(_keymapFile, """{ "version": 1, "bindings": { "Workspace.SwapPanes": "Ctrl+Shift+U" } }""");

        var map = KeyMap.LoadWithOverrides(_keymapFile);

        map.GestureFor(KeyActions.SwapPanes).Should().Be("Ctrl+Shift+U");
        map.GestureFor(KeyActions.CopyToOtherPane).Should().Be("F5", "다른 액션은 기본값 유지");
    }

    [Fact]
    public void LoadWithOverrides_IgnoresUnknownActionsAndBlankGestures()
    {
        File.WriteAllText(_keymapFile, """{ "bindings": { "Nope.Unknown": "F1", "Nav.Refresh": "  " } }""");

        var map = KeyMap.LoadWithOverrides(_keymapFile);

        map.Bindings.Should().NotContainKey("Nope.Unknown");
        map.GestureFor(KeyActions.Refresh).Should().Be("Ctrl+R");
    }

    [Fact]
    public void LoadWithOverrides_CorruptFile_FallsBackToDefaults()
    {
        File.WriteAllText(_keymapFile, "not json at all");

        var map = KeyMap.LoadWithOverrides(_keymapFile);

        map.Bindings.Should().BeEquivalentTo(KeyMap.CreateDefault().Bindings);
    }
}

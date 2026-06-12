using System.Windows.Input;
using Explorer.App.Input;
using FluentAssertions;

namespace Explorer.App.Tests.Input;

public sealed class GestureParserTests
{
    [Fact]
    public void Parse_FunctionKeyWithoutModifier()
    {
        GestureParser.Parse("F5").Should().Equal((ModifierKeys.None, Key.F5));
    }

    [Fact]
    public void Parse_PlainTab_IsAllowed()
    {
        // KeyGestureConverter는 수정자 없는 Tab을 거부하지만 우리 파서는 허용해야 한다 (TC 페인 전환).
        GestureParser.Parse("Tab").Should().Equal((ModifierKeys.None, Key.Tab));
    }

    [Fact]
    public void Parse_ModifierCombination()
    {
        GestureParser.Parse("Ctrl+Shift+D").Should().Equal(
            (ModifierKeys.Control | ModifierKeys.Shift, Key.D));
    }

    [Fact]
    public void Parse_MultipleGestures_SplitBySemicolon()
    {
        GestureParser.Parse("Ctrl+Right;Ctrl+Left").Should().Equal(
            (ModifierKeys.Control, Key.Right),
            (ModifierKeys.Control, Key.Left));
    }

    [Theory]
    [InlineData("Ctrl+U", ModifierKeys.Control, Key.U)]
    [InlineData("Alt+Up", ModifierKeys.Alt, Key.Up)]
    [InlineData("ctrl+r", ModifierKeys.Control, Key.R)]
    [InlineData("Ctrl+5", ModifierKeys.Control, Key.D5)]
    [InlineData("Del", ModifierKeys.None, Key.Delete)]
    [InlineData("Enter", ModifierKeys.None, Key.Return)]
    public void Parse_CommonGestures(string text, ModifierKeys expectedModifiers, Key expectedKey)
    {
        GestureParser.Parse(text).Should().Equal((expectedModifiers, expectedKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Foo+X")]
    [InlineData("NotAKey")]
    public void Parse_InvalidInput_GivesEmpty(string? text)
    {
        GestureParser.Parse(text).Should().BeEmpty();
    }

    [Fact]
    public void Parse_MixedValidAndInvalid_KeepsValidOnly()
    {
        GestureParser.Parse("F6;Bogus+Q;Ctrl+U").Should().Equal(
            (ModifierKeys.None, Key.F6),
            (ModifierKeys.Control, Key.U));
    }
}

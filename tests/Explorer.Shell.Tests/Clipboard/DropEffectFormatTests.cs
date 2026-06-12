using Explorer.Shell.Clipboard;
using FluentAssertions;

namespace Explorer.Shell.Tests.Clipboard;

public sealed class DropEffectFormatTests
{
    [Fact]
    public void Encode_Cut_ProducesMoveEffect()
    {
        var bytes = DropEffectFormat.Encode(cut: true);

        BitConverter.ToInt32(bytes, 0).Should().Be(2);
        DropEffectFormat.DecodeIsCut(bytes).Should().BeTrue();
    }

    [Fact]
    public void Encode_Copy_ProducesCopyLinkEffect()
    {
        var bytes = DropEffectFormat.Encode(cut: false);

        BitConverter.ToInt32(bytes, 0).Should().Be(5);
        DropEffectFormat.DecodeIsCut(bytes).Should().BeFalse();
    }

    [Fact]
    public void DecodeIsCut_HandlesMalformedData()
    {
        DropEffectFormat.DecodeIsCut(null).Should().BeFalse();
        DropEffectFormat.DecodeIsCut([]).Should().BeFalse();
        DropEffectFormat.DecodeIsCut([1, 2]).Should().BeFalse();
    }
}

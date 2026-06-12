using Explorer.Core.Formatting;
using FluentAssertions;

namespace Explorer.Core.Tests.Formatting;

public sealed class FileSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10485760, "10 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(146028888064, "136 GB")]
    [InlineData(-1, "")]
    public void Format_ProducesHumanReadableSize(long bytes, string expected)
    {
        FileSizeFormatter.Format(bytes).Should().Be(expected);
    }
}

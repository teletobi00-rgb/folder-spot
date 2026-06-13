using Explorer.Core.FileSystem;
using Explorer.Indexing.Usn;
using FluentAssertions;

namespace Explorer.Indexing.Tests.Usn;

public sealed class IndexSourceSelectorTests
{
    [Fact]
    public void NtfsFixedDrive_OptedIn_HelperAvailable_UsesUsn()
    {
        IndexSourceSelector.Select(DriveKind.Fixed, isNtfs: true, fastIndexingEnabled: true, helperAvailable: true)
            .Should().Be(IndexSourceMode.UsnFast);
    }

    [Fact]
    public void NotOptedIn_AlwaysFallback()
    {
        IndexSourceSelector.Select(DriveKind.Fixed, isNtfs: true, fastIndexingEnabled: false, helperAvailable: true)
            .Should().Be(IndexSourceMode.RecursiveFallback);
    }

    [Fact]
    public void HelperUnavailable_Fallback()
    {
        IndexSourceSelector.Select(DriveKind.Fixed, isNtfs: true, fastIndexingEnabled: true, helperAvailable: false)
            .Should().Be(IndexSourceMode.RecursiveFallback);
    }

    [Theory]
    [InlineData(DriveKind.Network)]
    [InlineData(DriveKind.Removable)]
    [InlineData(DriveKind.Optical)]
    public void NonFixedDrive_Fallback(DriveKind kind)
    {
        IndexSourceSelector.Select(kind, isNtfs: true, fastIndexingEnabled: true, helperAvailable: true)
            .Should().Be(IndexSourceMode.RecursiveFallback);
    }

    [Fact]
    public void NonNtfsFixedDrive_Fallback()
    {
        IndexSourceSelector.Select(DriveKind.Fixed, isNtfs: false, fastIndexingEnabled: true, helperAvailable: true)
            .Should().Be(IndexSourceMode.RecursiveFallback);
    }
}

using Explorer.Core.FileSystem;
using Explorer.Shell.Drives;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Shell.Tests.Drives;

/// <summary>실제 시스템 드라이브를 읽는 통합 스모크 테스트.</summary>
public sealed class DriveInfoDriveProviderTests
{
    [Fact]
    public void GetDrives_ReturnsAtLeastOneReadyFixedDrive()
    {
        var provider = new DriveInfoDriveProvider(NullLogger<DriveInfoDriveProvider>.Instance);

        var drives = provider.GetDrives();

        drives.Should().NotBeEmpty();
        drives.Should().Contain(d => d.IsReady && d.Kind == DriveKind.Fixed);

        var ready = drives.First(d => d.IsReady && d.Kind == DriveKind.Fixed);
        ready.RootPath.Should().EndWith(@":\");
        ready.TotalSize.Should().BePositive();
        ready.FreeSpace.Should().BeGreaterThanOrEqualTo(0);
    }
}

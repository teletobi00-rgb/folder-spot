using System.IO;
using Explorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace Explorer.Shell.Drives;

public sealed class DriveInfoDriveProvider : IDriveProvider
{
    private readonly ILogger<DriveInfoDriveProvider> _logger;

    public DriveInfoDriveProvider(ILogger<DriveInfoDriveProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var results = new List<DriveEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                results.Add(Map(drive));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "드라이브 정보를 읽지 못했습니다: {Drive}", drive.Name);
            }
        }

        return results;
    }

    private static DriveEntry Map(DriveInfo drive)
    {
        var kind = drive.DriveType switch
        {
            DriveType.Fixed => DriveKind.Fixed,
            DriveType.Removable => DriveKind.Removable,
            DriveType.Network => DriveKind.Network,
            DriveType.CDRom => DriveKind.Optical,
            DriveType.Ram => DriveKind.Ram,
            _ => DriveKind.Unknown,
        };

        var isReady = drive.IsReady;
        return new DriveEntry(
            RootPath: drive.Name,
            Label: isReady ? drive.VolumeLabel : string.Empty,
            Kind: kind,
            TotalSize: isReady ? drive.TotalSize : 0,
            FreeSpace: isReady ? drive.AvailableFreeSpace : 0,
            IsReady: isReady);
    }
}

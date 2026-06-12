namespace Explorer.Core.FileSystem;

public enum DriveKind
{
    Unknown,
    Fixed,
    Removable,
    Network,
    Optical,
    Ram,
}

/// <summary>드라이브 한 개의 불변 스냅샷. Label은 볼륨 레이블 그대로(없으면 빈 문자열).</summary>
public sealed record DriveEntry(
    string RootPath,
    string Label,
    DriveKind Kind,
    long TotalSize,
    long FreeSpace,
    bool IsReady);

public interface IDriveProvider
{
    IReadOnlyList<DriveEntry> GetDrives();
}

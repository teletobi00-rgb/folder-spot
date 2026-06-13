using Explorer.Core.FileSystem;

namespace Explorer.Indexing.Usn;

public enum IndexSourceMode
{
    /// <summary>MFT 열거 + USN 저널 tailing (권한 헬퍼 필요). 시작 비용 거의 0.</summary>
    UsnFast,

    /// <summary>재귀 열거 + FileSystemWatcher. 권한 불필요한 1급 폴백.</summary>
    RecursiveFallback,
}

/// <summary>
/// 볼륨별 인덱싱 소스 선택. USN 고속 경로는 "옵트인 + NTFS 고정 드라이브 + 헬퍼 가용"이 모두 참일 때만,
/// 그 외에는 전부 권한 불필요한 재귀+FSW 폴백을 쓴다 (권한 없이도 완전 동작 보장).
/// </summary>
public static class IndexSourceSelector
{
    public static IndexSourceMode Select(
        DriveKind kind,
        bool isNtfs,
        bool fastIndexingEnabled,
        bool helperAvailable)
    {
        if (!fastIndexingEnabled || !helperAvailable)
        {
            return IndexSourceMode.RecursiveFallback;
        }

        // USN 저널/MFT는 로컬 NTFS 고정 볼륨 전용 — 네트워크/이동식/비NTFS는 폴백.
        if (kind != DriveKind.Fixed || !isNtfs)
        {
            return IndexSourceMode.RecursiveFallback;
        }

        return IndexSourceMode.UsnFast;
    }

    /// <summary>경로의 볼륨이 NTFS인지 (실패/미상이면 false → 폴백).</summary>
    public static bool IsNtfsVolume(string rootPath)
    {
        try
        {
            return string.Equals(new DriveInfo(rootPath).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or DriveNotFoundException)
        {
            return false;
        }
    }
}

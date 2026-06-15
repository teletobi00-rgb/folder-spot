using System.Runtime.InteropServices;

namespace Explorer.Shell.FileOperations;

/// <summary>
/// 매핑된 네트워크 드라이브 경로(예: <c>Z:\dir\file</c>)를 UNC(<c>\\server\share\dir\file</c>)로 바꾼다.
/// 권한 상승된 셸 작업은 별도 프로세스에서 도는데, 그 프로세스는 사용자 토큰의 드라이브 매핑을
/// 상속받지 못한다(토큰별 DosDevices 격리). 그래서 보호 위치로 복사할 때 드라이브 문자 소스 경로를
/// "지정된 경로를 찾을 수 없습니다(0x80070003)"로 실패한다. SMB 자격증명은 로그온 세션 단위라
/// 토큰 간 공유되므로, UNC로 변환하면 권한 상승 프로세스도 동일 자격으로 접근할 수 있다.
/// 로컬 드라이브/이미 UNC인 경로는 그대로 둔다(예외 없이 graceful).
/// </summary>
public static class MappedDrivePathResolver
{
    private const int NoError = 0;
    private const int ErrorMoreData = 234;

    /// <summary>매핑된 네트워크 드라이브면 UNC로, 아니면 원본 그대로 반환한다.</summary>
    public static string ToUncIfMappedDrive(string path)
    {
        // "X:\..." 형태(드라이브 문자 + 콜론)만 대상. UNC(\\..)·확장경로(\\?\..)·상대경로는 제외.
        if (string.IsNullOrEmpty(path) || path.Length < 2 || path[1] != ':')
        {
            return path;
        }

        var drive = path[..2]; // "Z:"
        var unc = TryGetUncForDrive(drive);
        if (unc is null)
        {
            return path; // 로컬 드라이브이거나 매핑이 아님
        }

        var rest = path[2..].TrimStart('\\', '/'); // "dir\file"
        return rest.Length == 0 ? unc : $"{unc.TrimEnd('\\')}\\{rest}";
    }

    private static string? TryGetUncForDrive(string drive)
    {
        var length = 260;
        var buffer = new char[length];
        var result = WNetGetConnection(drive, buffer, ref length);
        if (result == ErrorMoreData)
        {
            buffer = new char[length];
            result = WNetGetConnection(drive, buffer, ref length);
        }

        if (result != NoError)
        {
            return null; // ERROR_NOT_CONNECTED 등(로컬 드라이브) → 호출자가 원본 경로 유지
        }

        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end >= 0 ? end : buffer.Length);
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetGetConnection(string localName, [Out] char[] remoteName, ref int length);
}

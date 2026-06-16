using System.Runtime.InteropServices;

namespace Explorer.Core;

/// <summary>프로세스 메모리를 OS에 반환하는 헬퍼(워킹셋 트림). Windows 전용 — 실패는 조용히 무시한다.</summary>
public static class ProcessMemory
{
    /// <summary>
    /// 현재 프로세스의 워킹셋을 비워 OS에 반환한다(다음 접근 시 페이지 폴트로 복구).
    /// 대규모 작업 직후나 트레이로 숨겨 유휴 상태가 될 때 호출해 거주 메모리를 낮춘다.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            EmptyWorkingSet(GetCurrentProcess());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // psapi/kernel32 부재(비 Windows 등) — 트림은 최적화일 뿐이므로 무시.
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
}

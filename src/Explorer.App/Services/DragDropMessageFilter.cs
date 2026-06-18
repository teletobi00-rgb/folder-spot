using System.Runtime.InteropServices;

namespace Explorer.App.Services;

/// <summary>
/// 관리자 권한으로 실행된 창이 일반 권한 프로세스(예: 탐색기)에서의 드래그앤드롭을 받도록
/// UIPI 메시지 필터를 연다. 권한 상승하지 않은 창에서는 무해하다(성공하거나 무효과).
/// </summary>
internal static class DragDropMessageFilter
{
    private const uint WM_DROPFILES = 0x0233;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_COPYGLOBALDATA = 0x0049; // OLE 드래그앤드롭이 전역 데이터를 전달할 때 쓰는 메시지
    private const uint MSGFLT_ALLOW = 1;

    public static void Allow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr changeInfo);
}

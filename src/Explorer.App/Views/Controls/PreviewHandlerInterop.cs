using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Explorer.App.Views.Controls;

/// <summary>IPreviewHandler 호스팅에 필요한 최소 COM 인터페이스/네이티브 호출 모음.</summary>
internal static class PreviewHandlerInterop
{
    public static readonly Guid IID_IPreviewHandler = new("8895b1c6-b41f-4c1c-a562-0d564250836f");

    public const uint ClsctxInprocServer = 0x1;
    public const uint ClsctxLocalServer = 0x4;

    // STGM_READ | STGM_SHARE_DENY_WRITE — 핸들러가 파일을 읽기 전용/공유로 열게 한다.
    // (단독 STGM_READ만 주면 일부 핸들러가 쓰기 거부 없이 열어 "사용 중" 충돌을 일으킨다.)
    public const uint StgmRead = 0x0;
    public const uint StgmShareDenyWrite = 0x20;
    public const uint StgmReadShareDenyWrite = StgmRead | StgmShareDenyWrite;

    public const int WsChild = unchecked((int)0x40000000);
    public const int WsVisible = 0x10000000;
    public const int WsClipChildren = 0x02000000;

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppv);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int SHCreateStreamOnFileEx(
        string pszFile, uint grfMode, uint dwAttributes, [MarshalAs(UnmanagedType.Bool)] bool fCreate,
        IntPtr pstmTemplate, out IStream ppstm);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
        int x, int y, int width, int height, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out PreviewRect lpRect);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PreviewRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

/// <summary>실제 OLE 미리보기 핸들러. 등록된 셸 확장이 지정 HWND에 콘텐츠를 그린다.</summary>
[ComImport]
[Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPreviewHandler
{
    void SetWindow(IntPtr hwnd, ref PreviewRect prc);

    void SetRect(ref PreviewRect prc);

    void DoPreview();

    void Unload();

    void SetFocus();

    void QueryFocus(out IntPtr phwnd);

    [PreserveSig]
    uint TranslateAccelerator(IntPtr pmsg);
}

/// <summary>파일 경로로 초기화하는 핸들러(가장 단순 — 대부분 지원).</summary>
[ComImport]
[Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
}

/// <summary>스트림으로 초기화하는 핸들러(파일 초기화 미지원 핸들러용 폴백).</summary>
[ComImport]
[Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithStream
{
    void Initialize(IStream pstream, uint grfMode);
}

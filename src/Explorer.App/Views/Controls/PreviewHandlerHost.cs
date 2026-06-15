using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;
using Explorer.App.Services.Preview;
using Serilog;
using static Explorer.App.Views.Controls.PreviewHandlerInterop;

namespace Explorer.App.Views.Controls;

/// <summary>
/// 등록된 IPreviewHandler를 호스팅하는 WPF HwndHost. <see cref="FilePath"/>를 설정하면 해당 파일의
/// 실제 OLE 미리보기(Office/PDF 등)를 자식 HWND에 그린다. 안정성을 위해 우선 out-of-proc
/// (CLSCTX_LOCAL_SERVER → prevhost.exe 대리)로 인스턴스화한다 — 핸들러가 죽어도 우리 프로세스를
/// 손상시키지 않는다(등록이 in-proc 전용이면 폴백).
/// 네트워크/매핑 드라이브 경로는 ShellPreviewRenderer가 미리 걸러 파일 정보로 폴백하므로
/// 여기에는 로컬 경로만 들어온다(out-of-proc 대리가 네트워크 세션을 못 가져 렌더 실패하기 때문).
/// </summary>
internal sealed class PreviewHandlerHost : HwndHost
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(PreviewHandlerHost),
        new PropertyMetadata(null, OnFilePathChanged));

    private IntPtr _hostHwnd;
    private object? _handlerComObject;
    private IPreviewHandler? _handler;
    private IStream? _stream;
    private bool _previewStarted;

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public PreviewHandlerHost()
    {
        // 레이아웃으로 크기가 확정되면 미리보기를 시작/리사이즈한다(OnWindowPositionChanged 누락 대비).
        SizeChanged += (_, _) => ApplyBounds();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // 기본 HwndHost는 (0,0)을 원해 호스트 창이 0×0이 되고 DoPreview가 빈 화면으로 굳는다.
        // 미리보기 영역(유한 크기)을 가득 채우도록 가용 크기를 그대로 요청한다.
        var width = double.IsInfinity(availableSize.Width) ? 0d : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 0d : availableSize.Height;
        return new Size(width, height);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostHwnd = CreateWindowEx(
            0, "STATIC", null, WsChild | WsVisible | WsClipChildren,
            0, 0, 0, 0, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        LoadPreview();
        return new HandleRef(this, _hostHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        UnloadPreview();
        if (_hostHwnd != IntPtr.Zero)
        {
            DestroyWindow(_hostHwnd);
            _hostHwnd = IntPtr.Zero;
        }
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ApplyBounds();
    }

    /// <summary>
    /// 현재 호스트 창 크기를 핸들러에 반영한다. DoPreview는 <b>유효한 크기가 생긴 뒤 1회만</b> 호출한다 —
    /// BuildWindowCore는 레이아웃 전(0×0)에 불리므로 그때 DoPreview하면 빈 화면으로 굳는다.
    /// </summary>
    private void ApplyBounds()
    {
        if (_handler is null || _hostHwnd == IntPtr.Zero || !GetClientRect(_hostHwnd, out var rc))
        {
            return;
        }

        try
        {
            _handler.SetRect(ref rc);
        }
        catch (COMException)
        {
            // 리사이즈 실패는 무시 — 다음 로드/리사이즈에서 복구된다.
        }

        if (_previewStarted || rc.Right <= rc.Left || rc.Bottom <= rc.Top)
        {
            return;
        }

        try
        {
            _handler.DoPreview();
        }
        catch (COMException ex)
        {
            Log.Warning("OLE DoPreview 실패: 0x{Hr:X8}", ex.HResult);
        }

        // 성공/실패 무관하게 1회만 시도 — 영구 실패한 핸들러를 리사이즈마다 재호출하지 않는다.
        _previewStarted = true;
    }

    private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (PreviewHandlerHost)d;
        if (host._hostHwnd != IntPtr.Zero)
        {
            host.LoadPreview();
        }
    }

    private void LoadPreview()
    {
        UnloadPreview();

        var path = FilePath;
        if (string.IsNullOrEmpty(path) || _hostHwnd == IntPtr.Zero || !File.Exists(path))
        {
            return;
        }

        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var clsid = ShellPreviewHandlerResolver.ResolveClsid(ext);
        if (clsid is null)
        {
            return;
        }

        try
        {
            var handlerObject = CreateHandler(clsid.Value);
            if (handlerObject is null)
            {
                return;
            }

            _handlerComObject = handlerObject;
            _handler = (IPreviewHandler)handlerObject;

            if (!Initialize(handlerObject, path))
            {
                UnloadPreview();
                return;
            }

            GetClientRect(_hostHwnd, out var rc);
            _handler.SetWindow(_hostHwnd, ref rc);

            // 크기가 이미 유효하면 즉시 미리보기, 아니면 OnWindowPositionChanged/SizeChanged에서 시작.
            ApplyBounds();
        }
        catch (COMException ex)
        {
            Log.Warning("OLE 미리보기 호스팅 실패: 0x{Hr:X8} — {Path}", ex.HResult, path);
            UnloadPreview();
        }
        catch (InvalidCastException)
        {
            UnloadPreview();
        }
    }

    private static object? CreateHandler(Guid clsid)
    {
        // out-of-proc(대리 호스트) 우선 — 핸들러 크래시 격리. 등록이 in-proc 전용이면 폴백.
        var hr = CoCreateInstance(clsid, IntPtr.Zero, ClsctxLocalServer, IID_IPreviewHandler, out var obj);
        if (hr == 0 && obj is not null)
        {
            return obj;
        }

        hr = CoCreateInstance(clsid, IntPtr.Zero, ClsctxInprocServer, IID_IPreviewHandler, out obj);
        if (hr == 0 && obj is not null)
        {
            return obj;
        }

        Log.Warning("OLE 핸들러 생성 실패: 0x{Hr:X8}", hr);
        return null;
    }

    private bool Initialize(object handlerObject, string path)
    {
        if (handlerObject is IInitializeWithFile initFile)
        {
            initFile.Initialize(path, StgmReadShareDenyWrite);
            return true;
        }

        if (handlerObject is IInitializeWithStream initStream
            && SHCreateStreamOnFileEx(path, StgmReadShareDenyWrite, 0, false, IntPtr.Zero, out var stream) == 0)
        {
            _stream = stream;
            initStream.Initialize(stream, StgmReadShareDenyWrite);
            return true;
        }

        return false;
    }

    private void UnloadPreview()
    {
        _previewStarted = false;

        if (_handler is not null)
        {
            try
            {
                _handler.Unload();
            }
            catch (COMException)
            {
                // 호스트가 이미 사라졌을 수 있다 — 무시하고 참조만 해제한다.
            }

            _handler = null;
        }

        if (_handlerComObject is not null)
        {
            Marshal.FinalReleaseComObject(_handlerComObject);
            _handlerComObject = null;
        }

        if (_stream is not null)
        {
            Marshal.FinalReleaseComObject(_stream);
            _stream = null;
        }
    }
}

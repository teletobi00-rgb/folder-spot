using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Explorer.Core.FileOperations;
using Microsoft.Extensions.Logging;

namespace Explorer.Shell.Clipboard;

/// <summary>WPF Clipboard 기반 구현. 탐색기와 양방향 호환(CF_HDROP + Preferred DropEffect).</summary>
public sealed class WpfFileClipboardService : IFileClipboardService
{
    private readonly ILogger<WpfFileClipboardService> _logger;

    public WpfFileClipboardService(ILogger<WpfFileClipboardService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public bool HasFiles
    {
        get
        {
            try
            {
                return System.Windows.Clipboard.ContainsFileDropList();
            }
            catch (COMException ex)
            {
                _logger.LogDebug(ex, "클립보드 조회 실패");
                return false;
            }
        }
    }

    public void SetFiles(IReadOnlyList<string> paths, bool cut)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            var fileList = new StringCollection();
            fileList.AddRange([.. paths]);

            var data = new DataObject();
            data.SetFileDropList(fileList);
            data.SetData(DropEffectFormat.FormatName, new MemoryStream(DropEffectFormat.Encode(cut)));
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }
        catch (COMException ex)
        {
            // 다른 앱이 클립보드를 잠근 경우 — 흔하고 일시적이라 조용히 로그만 남긴다.
            _logger.LogWarning(ex, "클립보드 쓰기 실패");
        }
    }

    public FileClipboardContent? GetFiles()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsFileDropList())
            {
                return null;
            }

            var fileList = System.Windows.Clipboard.GetFileDropList();
            var paths = fileList.Cast<string>().Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (paths.Length == 0)
            {
                return null;
            }

            var isCut = false;
            if (System.Windows.Clipboard.GetData(DropEffectFormat.FormatName) is MemoryStream stream)
            {
                isCut = DropEffectFormat.DecodeIsCut(stream.ToArray());
            }

            return new FileClipboardContent(paths, isCut);
        }
        catch (COMException ex)
        {
            _logger.LogDebug(ex, "클립보드 읽기 실패");
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            System.Windows.Clipboard.Clear();
        }
        catch (COMException ex)
        {
            _logger.LogDebug(ex, "클립보드 비우기 실패");
        }
    }
}

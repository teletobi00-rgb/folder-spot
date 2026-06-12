using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Explorer.Core.Caching;
using Explorer.Core.FileSystem;
using Explorer.Shell.Threading;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace Explorer.Shell.Icons;

/// <summary>SHGetFileInfo 기반 셸 아이콘 공급자. 확장자 단위 LRU 캐시로 디스크 접근을 최소화한다.</summary>
public sealed class ShellIconProvider : IShellIconProvider, IDisposable
{
    private const int CacheCapacity = 2048;

    private readonly LruCache<string, ImageSource> _cache = new(CacheCapacity);
    private readonly StaWorker _staWorker = new("Explorer.IconLoader");
    private readonly ILogger<ShellIconProvider> _logger;

    public ShellIconProvider(ILogger<ShellIconProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<ImageSource?> GetIconAsync(FileEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var key = IconCacheKey.For(entry);
        if (_cache.TryGet(key, out var cached))
        {
            return cached;
        }

        // SHGetFileInfo는 셸 COM을 쓰므로 전용 STA 스레드에서 실행한다 (스레드풀=MTA에서는 일부 확장이 조용히 실패).
        var icon = await _staWorker.RunAsync(() => LoadIcon(entry)).ConfigureAwait(false);
        if (icon is not null)
        {
            _cache.Set(key, icon);
        }

        return icon;
    }

    public void Dispose() => _staWorker.Dispose();

    private BitmapSource? LoadIcon(FileEntry entry)
    {
        try
        {
            var shfi = new Shell32.SHFILEINFO();
            var flags = Shell32.SHGFI.SHGFI_ICON | Shell32.SHGFI.SHGFI_SMALLICON;
            string queryPath;
            FileAttributes attributes;

            if (entry.IsDirectory)
            {
                flags |= Shell32.SHGFI.SHGFI_USEFILEATTRIBUTES;
                queryPath = entry.FullPath;
                attributes = FileAttributes.Directory;
            }
            else if (IconCacheKey.IsExtensionScoped(entry))
            {
                // 디스크 접근 없이 확장자만으로 종류 아이콘을 가져온다.
                flags |= Shell32.SHGFI.SHGFI_USEFILEATTRIBUTES;
                queryPath = "dummy." + entry.Extension;
                attributes = FileAttributes.Normal;
            }
            else
            {
                queryPath = entry.FullPath;
                attributes = FileAttributes.Normal;
            }

            var result = Shell32.SHGetFileInfo(queryPath, attributes, ref shfi, Marshal.SizeOf<Shell32.SHFILEINFO>(), flags);
            if (result == IntPtr.Zero || shfi.hIcon.IsNull)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    (IntPtr)shfi.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon((IntPtr)shfi.hIcon);
            }
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "아이콘 로드 실패: {Path}", entry.FullPath);
            return null;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

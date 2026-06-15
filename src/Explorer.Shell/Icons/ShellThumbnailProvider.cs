using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Explorer.Core.Caching;
using Explorer.Shell.Threading;
using Microsoft.Extensions.Logging;
using Vanara.Windows.Shell;

namespace Explorer.Shell.Icons;

/// <summary>경로의 셸 썸네일(이미지·문서 미리보기, 없으면 아이콘)을 지정 크기로 가져온다.</summary>
public interface IShellThumbnailProvider
{
    /// <summary>실패 시 null. 반환 이미지는 Frozen이라 스레드 안전하다.</summary>
    Task<ImageSource?> GetThumbnailAsync(string path, int size, CancellationToken cancellationToken = default);
}

/// <summary>IShellItemImageFactory(Vanara ShellItem.GetImage) 기반. 경로+크기 LRU 캐시 + STA 워커.</summary>
public sealed class ShellThumbnailProvider : IShellThumbnailProvider, IDisposable
{
    private const int CacheCapacity = 512;

    private readonly LruCache<string, ImageSource> _cache = new(CacheCapacity);
    private readonly StaWorker _worker = new("Explorer.ThumbLoader");
    private readonly ILogger<ShellThumbnailProvider> _logger;

    public ShellThumbnailProvider(ILogger<ShellThumbnailProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<ImageSource?> GetThumbnailAsync(string path, int size, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        cancellationToken.ThrowIfCancellationRequested();

        var key = $"{size}:{path}";
        if (_cache.TryGet(key, out var cached))
        {
            return cached;
        }

        var image = await _worker.RunAsync(() => Load(path, size)).ConfigureAwait(false);
        if (image is not null)
        {
            _cache.Set(key, image);
        }

        return image;
    }

    public void Dispose() => _worker.Dispose();

    private BitmapSource? Load(string path, int size)
    {
        try
        {
            using var item = new ShellItem(path);
            // 기본(ResizeToFit): 썸네일이 있으면 썸네일, 없으면 아이콘을 크기에 맞춰 준다.
            using var hbitmap = item.GetImage(new System.Drawing.Size(size, size), ShellItemGetImageOptions.ResizeToFit);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap.DangerousGetHandle(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or ExternalException)
        {
            _logger.LogDebug(ex, "썸네일 로드 실패: {Path}", path);
            return null;
        }
    }
}

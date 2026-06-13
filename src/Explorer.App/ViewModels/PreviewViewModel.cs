using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Explorer.Core.Formatting;
using Explorer.Preview;

namespace Explorer.App.ViewModels;

/// <summary>미리보기 한 화면의 바인딩 표면. PreviewResult를 WPF가 그릴 수 있는 형태로 변환한다.</summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    private const int MaxImageDecodeWidth = 1920;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind))]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(FooterText))]
    private PreviewResult _result = PreviewResult.None();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ImageSource? _imageSource;

    [ObservableProperty]
    private Uri? _mediaSource;

    [ObservableProperty]
    private IReadOnlyList<ArchiveEntryInfo> _archiveEntries = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _infoLines = [];

    private int _applyGeneration;

    public PreviewKind Kind => Result.Kind;

    public string DisplayName => Result.DisplayName;

    public string? ErrorText => Result.ErrorMessage;

    /// <summary>하단 메타데이터 줄 (인코딩/잘림/항목 수 등).</summary>
    public string FooterText => Result.Kind switch
    {
        PreviewKind.Text => string.Join("  ·  ", new[]
        {
            Result.EncodingName,
            Result.LanguageHint,
            Result.Truncated ? "1MB까지만 표시" : null,
        }.Where(s => !string.IsNullOrEmpty(s))),
        PreviewKind.Archive => $"{Result.ArchiveEntries.Length:N0}개 항목"
            + (Result.ArchiveTruncated ? " (일부만 표시)" : string.Empty),
        _ => string.Empty,
    };

    /// <summary>코디네이터 결과 적용 — UI 스레드에서 호출.</summary>
    public void Apply(PreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var generation = ++_applyGeneration;

        // 이전 미디어/이미지 핸들 해제
        MediaSource = null;
        ImageSource = null;
        ArchiveEntries = [];
        InfoLines = [];

        Result = result;

        switch (result.Kind)
        {
            case PreviewKind.Image:
                BeginDecodeImage(result.FilePath, generation);
                break;
            case PreviewKind.Media:
                MediaSource = TryCreateUri(result.FilePath);
                break;
            case PreviewKind.Archive:
                ArchiveEntries = result.ArchiveEntries;
                break;
            case PreviewKind.Info:
                InfoLines = result.InfoLines;
                break;
        }

        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(FooterText));
    }

    /// <summary>대용량 이미지 디코드는 UI 스레드를 막으므로 백그라운드에서 수행하고 결과만 마샬링한다.</summary>
    private void BeginDecodeImage(string path, int generation)
    {
        var uiContext = SynchronizationContext.Current;
        _ = Task.Run(() =>
        {
            var decoded = TryDecodeImage(path); // Freeze된 BitmapImage는 스레드 간 안전
            void Assign()
            {
                if (generation == _applyGeneration)
                {
                    ImageSource = decoded;
                }
            }

            if (uiContext is null)
            {
                Assign();
            }
            else
            {
                uiContext.Post(_ => Assign(), null);
            }
        });
    }

    public void Clear() => Apply(PreviewResult.None());

    private static BitmapImage? TryDecodeImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // 파일 잠금 방지
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.DecodePixelWidth = MaxImageDecodeWidth; // 대용량 다운스케일
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UnauthorizedAccessException
            or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static Uri? TryCreateUri(string path)
    {
        try
        {
            return new Uri(path);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    /// <summary>정보 미리보기 외에 형식별 추가 표시용 — InfoLine 포맷 헬퍼(현재는 직접 사용).</summary>
    internal static string FormatSize(long bytes) => FileSizeFormatter.Format(bytes);
}

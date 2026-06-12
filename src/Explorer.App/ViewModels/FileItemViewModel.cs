using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Explorer.Core.FileSystem;
using Explorer.Core.Formatting;
using Explorer.Shell.Icons;

namespace Explorer.App.ViewModels;

/// <summary>목록 한 행. 표시 문자열은 첫 접근 시 계산하고, 아이콘은 행이 실제로 그려질 때 지연 로드한다.</summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    private readonly IShellIconProvider _iconProvider;
    private string? _sizeText;
    private string? _dateModifiedText;
    private string? _attributesText;
    private ImageSource? _icon;
    private bool _iconRequested;

    /// <summary>잘라내기 표시 상태 (시각적 흐림 처리용).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameOpacity))]
    private bool _isCut;

    /// <summary>인라인 이름 변경 편집 중 여부.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>이름 변경 편집 텍스트.</summary>
    [ObservableProperty]
    private string _editName = string.Empty;

    public FileItemViewModel(FileEntry entry, IShellIconProvider iconProvider)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(iconProvider);
        Entry = entry;
        _iconProvider = iconProvider;
    }

    public FileEntry Entry { get; }

    public string Name => Entry.Name;

    public string Extension => Entry.Extension;

    public bool IsDirectory => Entry.IsDirectory;

    public double NameOpacity => IsCut ? 0.5 : 1.0;

    public string SizeText => _sizeText ??= Entry.IsDirectory ? string.Empty : FileSizeFormatter.Format(Entry.Size);

    public string DateModifiedText => _dateModifiedText ??=
        Entry.DateModified.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public string AttributesText => _attributesText ??= BuildAttributesText(Entry.Attributes);

    /// <summary>바인딩이 처음 읽는 순간(=가상화로 행이 생성될 때) 비동기 로드를 시작한다.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIconAsync();
            }

            return _icon;
        }
    }

    private async Task LoadIconAsync()
    {
        // getter는 항상 UI 스레드(바인딩)에서 호출되므로 ConfigureAwait(true)로 UI 컨텍스트에 복귀해
        // PropertyChanged가 UI 스레드에서 발생하도록 보장한다.
        var icon = await _iconProvider.GetIconAsync(Entry).ConfigureAwait(true);
        if (icon is not null)
        {
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
        }
    }

    private static string BuildAttributesText(FileAttributes attributes)
    {
        var builder = new StringBuilder(6);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            builder.Append('R');
        }

        if ((attributes & FileAttributes.Hidden) != 0)
        {
            builder.Append('H');
        }

        if ((attributes & FileAttributes.System) != 0)
        {
            builder.Append('S');
        }

        if ((attributes & FileAttributes.Archive) != 0)
        {
            builder.Append('A');
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            builder.Append('L');
        }

        if ((attributes & FileAttributes.Compressed) != 0)
        {
            builder.Append('C');
        }

        if ((attributes & FileAttributes.Encrypted) != 0)
        {
            builder.Append('E');
        }

        return builder.ToString();
    }
}

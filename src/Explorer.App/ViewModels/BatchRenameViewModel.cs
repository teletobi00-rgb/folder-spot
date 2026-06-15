using System.Globalization;
using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileOperations;

namespace Explorer.App.ViewModels;

/// <summary>
/// 다중 선택 파일 일괄 이름 변경. 찾기/바꾸기 + 접두/접미사 + 연번을 조합해 새 이름을 만들고
/// 실시간 미리보기를 보여준다. 적용은 항목별 RenameAsync로 수행한다.
/// </summary>
public sealed partial class BatchRenameViewModel : ObservableObject
{
    private readonly IFileOperationService _operations;
    private IReadOnlyList<FileItemViewModel> _targets = [];

    [ObservableProperty]
    private string _find = string.Empty;

    [ObservableProperty]
    private string _replace = string.Empty;

    [ObservableProperty]
    private string _prefix = string.Empty;

    [ObservableProperty]
    private string _suffix = string.Empty;

    [ObservableProperty]
    private bool _useSequence;

    [ObservableProperty]
    private int _startNumber = 1;

    [ObservableProperty]
    private int _padding = 2;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isApplying;

    [ObservableProperty]
    private double _progress;

    public ObservableCollection<RenamePreview> Previews { get; } = [];

    /// <summary>실제로 적용됐는지 — 호출자가 목록을 새로고침할지 판단한다.</summary>
    public bool Applied { get; private set; }

    public BatchRenameViewModel(IFileOperationService operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    /// <summary>대상 항목을 설정하고 미리보기를 계산한다.</summary>
    public void SetTargets(IReadOnlyList<FileItemViewModel> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _targets = targets;
        Recompute();
    }

    partial void OnFindChanged(string value) => Recompute();

    partial void OnReplaceChanged(string value) => Recompute();

    partial void OnPrefixChanged(string value) => Recompute();

    partial void OnSuffixChanged(string value) => Recompute();

    partial void OnUseSequenceChanged(bool value) => Recompute();

    partial void OnStartNumberChanged(int value) => Recompute();

    partial void OnPaddingChanged(int value) => Recompute();

    private void Recompute()
    {
        Previews.Clear();
        var counter = StartNumber;
        foreach (var target in _targets)
        {
            Previews.Add(new RenamePreview(target.Name, ComputeName(target.Name, counter)));
            if (UseSequence)
            {
                counter++;
            }
        }
    }

    private string ComputeName(string original, int counter)
    {
        var baseName = Path.GetFileNameWithoutExtension(original);
        var extension = Path.GetExtension(original);

        if (Find.Length > 0)
        {
            baseName = baseName.Replace(Find, Replace, StringComparison.Ordinal);
        }

        var result = Prefix + baseName + Suffix;
        if (UseSequence)
        {
            result += counter.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Clamp(Padding, 1, 10), '0');
        }

        return result + extension;
    }

    private bool CanApply => !IsApplying && !Applied;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsApplying = true;
        var total = _targets.Count;
        var renamed = 0;
        var failed = 0;
        try
        {
            for (var i = 0; i < total; i++)
            {
                Progress = total == 0 ? 0 : i * 100.0 / total;
                StatusMessage = $"{i + 1} / {total} 처리 중…";

                var target = _targets[i];
                var newName = Previews[i].NewName;
                if (string.Equals(newName, target.Name, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(newName))
                {
                    continue;
                }

                var result = await _operations.RenameAsync(target.Entry.FullPath, newName).ConfigureAwait(true);
                if (result.Succeeded)
                {
                    renamed++;
                }
                else
                {
                    failed++;
                }
            }

            Progress = 100;
            Applied = renamed > 0;
            StatusMessage = failed == 0
                ? $"{renamed}개 이름 변경 완료"
                : $"{renamed}개 변경, {failed}개 실패(잘못된 이름/중복 등)";
        }
        finally
        {
            IsApplying = false;
        }
    }
}

/// <summary>일괄 이름 변경 미리보기 한 줄.</summary>
public sealed record RenamePreview(string OldName, string NewName)
{
    public bool Changed => !string.Equals(OldName, NewName, StringComparison.Ordinal);
}

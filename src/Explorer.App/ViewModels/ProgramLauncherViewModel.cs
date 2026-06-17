using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.App.Services;
using Explorer.Core.Settings;

namespace Explorer.App.ViewModels;

/// <summary>상단 빠른 실행 바 — 고정 프로그램을 표시·실행·편집하고 설정에 보존한다.</summary>
public sealed partial class ProgramLauncherViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    [ObservableProperty]
    private bool _isVisible;

    public ProgramLauncherViewModel(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        IsVisible = settings.Current.ShowProgramLauncher;
        foreach (var command in settings.Current.PinnedPrograms)
        {
            Items.Add(new PinnedProgramViewModel(command));
        }
    }

    public ObservableCollection<PinnedProgramViewModel> Items { get; } = [];

    /// <summary>설정 창에서 표시 여부가 바뀐 뒤 동기화한다.</summary>
    public void SyncVisibilityFromSettings() => IsVisible = _settings.Current.ShowProgramLauncher;

    [RelayCommand]
    private static void Launch(PinnedProgramViewModel? item)
    {
        if (item is not null)
        {
            ProgramResolver.Launch(item.Command);
        }
    }

    [RelayCommand]
    private void Remove(PinnedProgramViewModel? item)
    {
        if (item is not null && Items.Remove(item))
        {
            Persist();
        }
    }

    /// <summary>드롭/추가로 새 프로그램을 고정. 빈 값·중복은 무시.</summary>
    public void Add(string command) => AddPinned(command, customName: null);

    /// <summary>대상 경로/URI와(선택) 사용자 지정 이름으로 고정. 빈 값·중복(대상 기준)은 무시.</summary>
    public void AddPinned(string target, string? customName)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var normalized = target.Trim().Trim('"');
        if (Items.Any(item => string.Equals(item.Command, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(customName) ? null : customName.Replace('|', ' ').Trim();
        var stored = name is null ? normalized : name + "|" + normalized;
        Items.Add(new PinnedProgramViewModel(stored));
        Persist();
    }

    /// <summary>인라인 이름 변경 시작.</summary>
    [RelayCommand]
    private static void BeginRename(PinnedProgramViewModel? item)
    {
        if (item is not null)
        {
            item.EditName = item.DisplayName;
            item.IsRenaming = true;
        }
    }

    /// <summary>편집한 이름을 확정·저장한다.</summary>
    public void CommitRename(PinnedProgramViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.ApplyRename(item.EditName);
        item.IsRenaming = false;
        Persist();
    }

    private void Persist()
    {
        var stored = Items.Select(item => item.ToStored()).ToImmutableArray();
        _settings.Update(settings => settings with { PinnedPrograms = stored });
    }
}

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
    public void Add(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var normalized = command.Trim().Trim('"');
        if (Items.Any(item => string.Equals(item.Command, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Items.Add(new PinnedProgramViewModel(normalized));
        Persist();
    }

    private void Persist()
    {
        var commands = Items.Select(item => item.Command).ToImmutableArray();
        _settings.Update(settings => settings with { PinnedPrograms = commands });
    }
}

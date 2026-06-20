using System.IO;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileOperations;
using Explorer.Core.FileSystem;
using Explorer.Core.Operations;
using Explorer.Core.Sorting;
using Microsoft.Extensions.Logging;

namespace Explorer.App.ViewModels;

/// <summary>파일 작업 명령(복사/잘라내기/붙여넣기/삭제/이름변경/새 폴더/드롭) — FileListViewModel의 파셜.</summary>
public sealed partial class FileListViewModel
{
    private bool _operationInFlight;
    private DateTime _suppressExternalRefreshUntil = DateTime.MinValue;

    private bool HasSelection => SelectedItems.Count > 0;

    private bool HasSingleSelection => SelectedItems.Count == 1;

    private bool CanPaste => CurrentPath is not null && _clipboard.HasFiles;

    private bool CanCreateFolder => CurrentPath is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopySelection()
    {
        _clipboard.SetFiles(GetSelectedPaths(), cut: false);
        ClearCutMarks();
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CutSelection()
    {
        _clipboard.SetFiles(GetSelectedPaths(), cut: true);
        ClearCutMarks();
        foreach (var item in SelectedItems)
        {
            item.IsCut = true;
        }

        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteAsync()
    {
        var destination = CurrentPath;
        if (destination is null)
        {
            return;
        }

        var content = _clipboard.GetFiles();
        if (content is null)
        {
            return;
        }

        var result = await RunOperationAsync(() => _queue.EnqueueAsync(content.IsCut
            ? OperationRequest.Move(content.Paths, destination)
            : OperationRequest.Copy(content.Paths, destination)));

        if (result.Succeeded)
        {
            if (content.IsCut)
            {
                _clipboard.Clear();
                PasteCommand.NotifyCanExecuteChanged();
            }

            await ReloadAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DeleteSelectionAsync() => DeleteCoreAsync(permanent: false);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DeleteSelectionPermanentAsync() => DeleteCoreAsync(permanent: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddSelectionToFavorites()
    {
        var added = 0;
        foreach (var item in SelectedItems)
        {
            try
            {
                _favorites.Add(item.Entry.FullPath, item.IsDirectory);
                added++;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "즐겨찾기 추가 실패: {Path}", item.Entry.FullPath);
            }
        }

        StatusMessage = $"{added}개 항목을 즐겨찾기에 추가했습니다.";
    }

    /// <summary>선택한 파일들을 첨부해 Outlook 새 메일 작성 창을 연다(폴더는 제외).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ComposeOutlookMail()
    {
        var files = SelectedItems.Where(i => !i.IsDirectory).Select(i => i.Entry.FullPath).ToArray();
        if (files.Length == 0)
        {
            StatusMessage = "첨부할 파일을 선택하세요 (폴더는 제외).";
            return;
        }

        if (!Explorer.App.Services.OutlookMailService.NewMailWithAttachments(files))
        {
            StatusMessage = "Outlook으로 새 메일을 열지 못했습니다 (Outlook 설치 필요).";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void BeginRename()
    {
        if (SelectedItems is [{ } item])
        {
            item.EditName = item.Name;
            item.IsRenaming = true;
        }
    }

    [RelayCommand]
    private async Task CommitRenameAsync(FileItemViewModel? item)
    {
        if (item is null || !item.IsRenaming)
        {
            return;
        }

        // 첫 await 전에 동기적으로 false로 내려야 Enter 커밋 직후 따라오는 LostFocus 커밋이
        // 위의 IsRenaming 가드에서 걸러진다 — 이 줄을 await 뒤로 옮기면 이중 커밋이 발생한다.
        item.IsRenaming = false;
        var newName = item.EditName.Trim();
        if (newName.Length == 0 || string.Equals(newName, item.Name, StringComparison.Ordinal))
        {
            return;
        }

        if (!FileNameRules.IsValid(newName, out var reason))
        {
            StatusMessage = reason;
            return;
        }

        if (Items.Any(i => !ReferenceEquals(i, item)
            && string.Equals(i.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"'{newName}' 이름이 이미 사용 중입니다.";
            return;
        }

        var result = await RunOperationAsync(() => _operations.RenameAsync(item.Entry.FullPath, newName));
        if (result.Succeeded)
        {
            ReplaceWithRenamedItem(item, newName);
        }
    }

    [RelayCommand]
    private static void CancelRename(FileItemViewModel? item)
    {
        if (item is not null)
        {
            item.IsRenaming = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateFolder))]
    private async Task CreateFolderAsync()
    {
        var parent = CurrentPath;
        if (parent is null)
        {
            return;
        }

        var name = FileNameRules.GenerateUniqueName(Items.Select(i => i.Name), "새 폴더");
        var result = await RunOperationAsync(() => _operations.CreateFolderAsync(parent, name));
        if (!result.Succeeded)
        {
            return;
        }

        var entry = FileEntry.Create(
            Path.Combine(parent, name), name, isDirectory: true, size: 0,
            DateTime.Now, DateTime.Now, FileAttributes.Directory);
        var item = new FileItemViewModel(entry, _iconProvider);
        ResortWith(Items.Append(item));

        SelectedItem = item;
        SelectedItems = [item];
        item.EditName = name;
        item.IsRenaming = true;
    }

    /// <summary>외부(드래그앤드롭)에서 들어온 드롭 처리. 유효성은 DropRules로 검증한다.</summary>
    public async Task HandleDropAsync(IReadOnlyList<string> sourcePaths, string targetDir, DropOperation operation)
    {
        if (!DropRules.CanDrop(sourcePaths, targetDir, operation))
        {
            return;
        }

        var result = await RunOperationAsync(() => _queue.EnqueueAsync(operation == DropOperation.Move
            ? OperationRequest.Move(sourcePaths, targetDir)
            : OperationRequest.Copy(sourcePaths, targetDir)));

        if (result.Succeeded)
        {
            await ReloadAsync();
        }
    }

    private async Task DeleteCoreAsync(bool permanent)
    {
        var targets = SelectedItems.ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        var paths = targets.Select(t => t.Entry.FullPath).ToArray();
        var result = await RunOperationAsync(() => _queue.EnqueueAsync(OperationRequest.Delete(paths, permanent)));
        if (result.Succeeded)
        {
            RemoveItems(targets);
        }
    }

    private async Task<FileOperationResult> RunOperationAsync(Func<Task<FileOperationResult>> operation)
    {
        _operationInFlight = true;
        try
        {
            var result = await operation();
            if (!result.Succeeded && !result.Aborted)
            {
                StatusMessage = DescribeError(result);
            }

            return result;
        }
        finally
        {
            _operationInFlight = false;
            // 우리 작업이 일으킨 FileSystemWatcher 에코가 디바운스 후 도착할 시간만큼 외부 새로고침을 억제한다.
            _suppressExternalRefreshUntil = DateTime.UtcNow.AddMilliseconds(800);
        }
    }

    private bool IsExternalRefreshSuppressed() =>
        _operationInFlight || DateTime.UtcNow < _suppressExternalRefreshUntil;

    private string[] GetSelectedPaths() =>
        SelectedItems.Select(i => i.Entry.FullPath).ToArray();

    private void NotifySelectionCommands()
    {
        CopySelectionCommand.NotifyCanExecuteChanged();
        CutSelectionCommand.NotifyCanExecuteChanged();
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        DeleteSelectionPermanentCommand.NotifyCanExecuteChanged();
        BeginRenameCommand.NotifyCanExecuteChanged();
        AddSelectionToFavoritesCommand.NotifyCanExecuteChanged();
        ComposeOutlookMailCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
    }

    private void ClearCutMarks()
    {
        foreach (var item in Items)
        {
            item.IsCut = false;
        }
    }

    private void RemoveItems(IReadOnlyList<FileItemViewModel> toRemove)
    {
        var removal = toRemove.ToHashSet();
        Items = Items.Where(i => !removal.Contains(i)).ToArray();
        SelectedItem = null;
        SelectedItems = [];
        if (Items.Count == 0)
        {
            Status = FileListStatus.Empty;
        }
    }

    private void ReplaceWithRenamedItem(FileItemViewModel oldItem, string newName)
    {
        var entry = oldItem.Entry;
        var parent = PathUtils.GetParent(entry.FullPath);
        if (parent is null)
        {
            return;
        }

        var newEntry = FileEntry.Create(
            Path.Combine(parent, newName), newName, entry.IsDirectory, entry.Size,
            entry.DateModified, entry.DateCreated, entry.Attributes);
        var newItem = new FileItemViewModel(newEntry, _iconProvider);
        ResortWith(Items.Select(i => ReferenceEquals(i, oldItem) ? newItem : i));

        SelectedItem = newItem;
        SelectedItems = [newItem];
    }

    private void ResortWith(IEnumerable<FileItemViewModel> source)
    {
        var comparer = FileEntryComparers.Create(Sort);
        var list = source.ToList();
        list.Sort((a, b) => comparer.Compare(a.Entry, b.Entry));
        Items = list;
        Status = list.Count == 0 ? FileListStatus.Empty : FileListStatus.None;
    }

    private static string DescribeError(FileOperationResult result) => result.Error switch
    {
        FileOperationError.AccessDenied => "접근 권한이 없습니다.",
        FileOperationError.InUse => "파일이 다른 프로그램에서 사용 중입니다.",
        FileOperationError.PathNotFound => "경로를 찾을 수 없습니다.",
        FileOperationError.NameTooLong => "경로가 너무 깁니다.",
        FileOperationError.DiskFull => "디스크 공간이 부족합니다.",
        FileOperationError.AlreadyExists => "같은 이름이 이미 있습니다.",
        FileOperationError.InvalidName => result.Message ?? "이름이 올바르지 않습니다.",
        _ => result.Message ?? "작업을 완료하지 못했습니다.",
    };
}

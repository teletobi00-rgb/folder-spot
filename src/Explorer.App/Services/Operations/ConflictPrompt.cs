using System.Windows;
using Explorer.App.Views;
using Explorer.Core.FileOperations;

namespace Explorer.App.Services.Operations;

/// <summary>충돌 목록을 사용자에게 묻는다. 큐 워커 스레드에서 호출되므로 구현이 UI 마샬링을 책임진다.</summary>
public interface IConflictPrompt
{
    /// <returns>충돌별 결정. 사용자가 닫으면(전체 취소) null.</returns>
    Task<IReadOnlyDictionary<FileConflict, ConflictDecision>?> ResolveAsync(IReadOnlyList<FileConflict> conflicts);
}

public sealed class DialogConflictPrompt : IConflictPrompt
{
    public async Task<IReadOnlyDictionary<FileConflict, ConflictDecision>?> ResolveAsync(
        IReadOnlyList<FileConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return null;
        }

        return await dispatcher.InvokeAsync(() => ResolveOnUiThread(conflicts)).Task.ConfigureAwait(false);
    }

    private static Dictionary<FileConflict, ConflictDecision>? ResolveOnUiThread(
        IReadOnlyList<FileConflict> conflicts)
    {
        var decisions = new Dictionary<FileConflict, ConflictDecision>();

        for (var i = 0; i < conflicts.Count; i++)
        {
            var dialog = new ConflictDialog(conflicts[i], remainingCount: conflicts.Count - i - 1)
            {
                Owner = Application.Current?.MainWindow,
            };

            if (dialog.ShowDialog() != true || dialog.Decision is not { } decision)
            {
                return null; // 사용자 취소 → 작업 전체 취소
            }

            decisions[conflicts[i]] = decision;

            if (dialog.ApplyToAll)
            {
                for (var rest = i + 1; rest < conflicts.Count; rest++)
                {
                    decisions[conflicts[rest]] = decision;
                }

                break;
            }
        }

        return decisions;
    }
}

namespace Explorer.Core.FileOperations;

public sealed record FileClipboardContent(IReadOnlyList<string> Paths, bool IsCut);

/// <summary>탐색기 호환(CF_HDROP + Preferred DropEffect) 파일 클립보드. UI 스레드에서만 호출한다.</summary>
public interface IFileClipboardService
{
    void SetFiles(IReadOnlyList<string> paths, bool cut);

    FileClipboardContent? GetFiles();

    bool HasFiles { get; }

    void Clear();
}

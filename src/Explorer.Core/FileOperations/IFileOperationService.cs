namespace Explorer.Core.FileOperations;

/// <summary>
/// 파일 작업 추상화. 구현은 예외를 던지지 않고 <see cref="FileOperationResult"/>로 보고한다.
/// 진행/충돌 UI는 구현(셸)이 담당한다 — Phase 5에서 자체 큐 UI로 대체 예정.
/// </summary>
public interface IFileOperationService
{
    Task<FileOperationResult> CopyAsync(IReadOnlyList<string> sourcePaths, string destinationDir);

    Task<FileOperationResult> MoveAsync(IReadOnlyList<string> sourcePaths, string destinationDir);

    /// <param name="permanent">true면 휴지통을 거치지 않고 영구 삭제.</param>
    Task<FileOperationResult> DeleteAsync(IReadOnlyList<string> paths, bool permanent);

    Task<FileOperationResult> RenameAsync(string path, string newName);

    Task<FileOperationResult> CreateFolderAsync(string parentDir, string name);
}

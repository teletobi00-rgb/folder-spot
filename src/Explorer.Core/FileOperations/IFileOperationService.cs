namespace Explorer.Core.FileOperations;

/// <summary>
/// 파일 작업 추상화. 구현은 예외를 던지지 않고 <see cref="FileOperationResult"/>로 보고한다.
/// <paramref name="context"/>가 null이면 셸 기본 UI/동작(Phase 2), 지정하면 자체 큐 UI가
/// 진행/일시정지/취소/충돌 정책을 제어한다.
/// </summary>
public interface IFileOperationService
{
    Task<FileOperationResult> CopyAsync(
        IReadOnlyList<string> sourcePaths, string destinationDir, FileOperationContext? context = null);

    Task<FileOperationResult> MoveAsync(
        IReadOnlyList<string> sourcePaths, string destinationDir, FileOperationContext? context = null);

    /// <param name="paths">삭제 대상.</param>
    /// <param name="permanent">true면 휴지통을 거치지 않고 영구 삭제.</param>
    /// <param name="context">큐 실행 컨텍스트.</param>
    Task<FileOperationResult> DeleteAsync(
        IReadOnlyList<string> paths, bool permanent, FileOperationContext? context = null);

    Task<FileOperationResult> RenameAsync(string path, string newName);

    Task<FileOperationResult> CreateFolderAsync(string parentDir, string name);

    /// <summary>항목 하나를 대상 폴더로 옮기며 새 이름을 부여한다 (휴지통 복원 등 Undo 용).</summary>
    Task<FileOperationResult> MoveItemAsync(string sourcePath, string destinationDir, string? newName = null);
}

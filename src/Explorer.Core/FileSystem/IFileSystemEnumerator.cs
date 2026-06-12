namespace Explorer.Core.FileSystem;

public interface IFileSystemEnumerator
{
    /// <summary>
    /// 디렉터리의 직계 항목을 모두 나열한다(숨김/시스템 포함, 필터링은 호출자 책임).
    /// 디렉터리가 없으면 <see cref="DirectoryNotFoundException"/>,
    /// 접근이 거부되면 <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    Task<IReadOnlyList<FileEntry>> ListAsync(string directoryPath, CancellationToken cancellationToken = default);
}

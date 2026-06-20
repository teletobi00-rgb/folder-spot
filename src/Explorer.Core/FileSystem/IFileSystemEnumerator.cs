namespace Explorer.Core.FileSystem;

public interface IFileSystemEnumerator
{
    /// <summary>
    /// 디렉터리의 직계 항목을 모두 나열한다(숨김/시스템 포함, 필터링은 호출자 책임).
    /// 디렉터리가 없으면 <see cref="DirectoryNotFoundException"/>,
    /// 접근이 거부되면 <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    Task<IReadOnlyList<FileEntry>> ListAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 디렉터리 항목을 배치 단위로 스트리밍한다 — 전체 열거를 기다리지 않고 도착하는 대로 표시하기 위함
    /// (네트워크·대용량 폴더의 체감 로딩 개선). 오류 처리는 <see cref="ListAsync"/>와 동일하다.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<FileEntry>> StreamAsync(string directoryPath, CancellationToken cancellationToken = default);
}

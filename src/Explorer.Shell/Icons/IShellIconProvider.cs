using System.Windows.Media;
using Explorer.Core.FileSystem;

namespace Explorer.Shell.Icons;

public interface IShellIconProvider
{
    /// <summary>
    /// 항목의 셸 아이콘을 비동기로 가져온다. 반환되는 ImageSource는 Frozen 상태라 모든 스레드에서 쓸 수 있다.
    /// 실패하면 null (호출자는 placeholder를 유지).
    /// </summary>
    Task<ImageSource?> GetIconAsync(FileEntry entry, CancellationToken cancellationToken = default);
}

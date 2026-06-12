namespace Explorer.Core.FileSystem;

/// <summary>
/// 현재 폴더의 외부 변경(다른 앱/탐색기에 의한 생성·삭제·이름변경)을 감지한다.
/// 이벤트는 디바운스되어 발생하며, 발생 스레드는 보장하지 않는다 — 구독자가 UI 디스패치를 책임진다.
/// </summary>
public interface IFolderWatcher : IDisposable
{
    /// <summary>지정 폴더 감시 시작(기존 감시는 교체). 감시 불가 폴더면 조용히 비활성화된다.</summary>
    void Watch(string directoryPath);

    void StopWatching();

    event EventHandler? ChangesDetected;
}

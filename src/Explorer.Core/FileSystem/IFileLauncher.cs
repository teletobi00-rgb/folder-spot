namespace Explorer.Core.FileSystem;

public interface IFileLauncher
{
    /// <summary>연결 프로그램으로 파일을 연다. 실패 시 예외를 던진다(연결 없음, 사용자 취소 등).</summary>
    void Launch(string fullPath);
}

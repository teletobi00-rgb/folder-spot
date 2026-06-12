namespace Explorer.App.Services;

/// <summary>
/// 트레이 상주 수명 관리: 메인 창 닫기 = 트레이로 숨김(핫키/인덱싱 유지),
/// 진짜 종료는 트레이 메뉴를 통해서만.
/// </summary>
public sealed class AppLifecycle
{
    public bool ExitRequested { get; private set; }

    public void RequestExit()
    {
        ExitRequested = true;
        System.Windows.Application.Current?.Shutdown();
    }
}

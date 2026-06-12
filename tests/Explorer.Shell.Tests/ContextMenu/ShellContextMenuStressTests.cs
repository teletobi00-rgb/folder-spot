using Explorer.Shell.Threading;
using FluentAssertions;
using Vanara.Windows.Shell;

namespace Explorer.Shell.Tests.ContextMenu;

/// <summary>
/// 힙 손상(0xc0000374) 진단: 손상이 있으면 테스트 호스트가 통째로 크래시한다('러너 사망'으로 표시).
/// </summary>
public sealed class ShellContextMenuStressTests
{
    // 진단 결과 기록(2026-06-12): 이 변형은 테스트 호스트를 크래시시킴 — populate+dispose 사이클이 힙을 손상.
    [Fact(Skip = "힙 손상 재현용 진단 테스트 — 수동 실행 전용 (호스트 크래시)")]
    public async Task PopulateAndDispose_Repeated_CorruptsHeap_KnownIssue()
    {
        using var worker = new StaWorker("menu-stress");
        var targetFile = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var targetDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        for (var i = 0; i < 15; i++)
        {
            var path = i % 2 == 0 ? targetFile : targetDir;
            await worker.RunAsync(() =>
            {
                using var item = new ShellItem(path);
                var menu = ShellContextMenu.CreateFromItems([item], out var keepAlive);
                try
                {
                    menu.PopulateMenu();
                }
                finally
                {
                    menu.Dispose();
                    keepAlive.Dispose();
                }

                return true;
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // 진단 결과 기록(2026-06-12): 누수 변형조차 호스트를 크래시시킴 → teardown이 아니라
    // QueryContextMenu 자체(이 머신의 특정 셸 확장)가 비탐색기 호스트에서 힙을 손상시킨다.
    // 대응: 기본 우클릭은 자체 메뉴, 네이티브 메뉴는 옵트인. 근본 해결은 out-of-proc 호스트(백로그).
    [Fact(Skip = "힙 손상 재현용 진단 테스트 — 수동 실행 전용 (호스트 크래시)")]
    public async Task PopulateWithoutDispose_Repeated_AlsoCorruptsHeap_KnownIssue()
    {
        using var worker = new StaWorker("menu-stress-leak");
        var targetFile = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var targetDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 의도적 누수: 아무것도 해제하지 않고 전부 살려둔다 (GC 수집도 방지).
        var keepAliveForever = new List<object>();

        for (var i = 0; i < 15; i++)
        {
            var path = i % 2 == 0 ? targetFile : targetDir;
            var leaked = await worker.RunAsync(() =>
            {
                var item = new ShellItem(path);
                var menu = ShellContextMenu.CreateFromItems([item], out var keepAlive);
                menu.PopulateMenu();
                return (object)(item, menu, keepAlive);
            });

            keepAliveForever.Add(leaked);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        keepAliveForever.Should().HaveCount(15);
    }
}

using Explorer.Core.FileOperations;
using Explorer.Shell.FileOperations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explorer.Shell.Tests.FileOperations;

/// <summary>FileOperationContext 경유 실행의 실제 셸 엔진 통합 검증 (진행/완료 수집/충돌 정책/취소).</summary>
public sealed class ShellFileOperationServiceContextTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _destDir;
    private readonly ShellFileOperationService _service;

    private sealed class CollectingEvents : IOperationEvents
    {
        public List<int> ProgressReports { get; } = [];

        public List<CompletedItem> Completed { get; } = [];

        public void OnProgress(int percent) => ProgressReports.Add(percent);

        public void OnItemCompleted(CompletedItem item)
        {
            lock (Completed)
            {
                Completed.Add(item);
            }
        }
    }

    public ShellFileOperationServiceContextTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExplorerTests", Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_tempDir, "src");
        _destDir = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
        _service = new ShellFileOperationService(
            () => 0, NullLogger<ShellFileOperationService>.Instance, silent: true);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string MakeSource(string name, string content = "data")
    {
        var path = Path.Combine(_sourceDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task CopyWithContext_ReportsCompletedItems_WithActualPaths()
    {
        var source = MakeSource("a.txt");
        var events = new CollectingEvents();

        var result = await _service.CopyAsync([source], _destDir, new FileOperationContext
        {
            Collision = CollisionOption.Overwrite,
            Events = events,
        });

        result.Succeeded.Should().BeTrue();
        File.Exists(Path.Combine(_destDir, "a.txt")).Should().BeTrue();
        events.Completed.Should().ContainSingle(c =>
            c.Kind == OperationKind.Copy
            && c.SourcePath == source
            && c.NewPath == Path.Combine(_destDir, "a.txt"));
    }

    [Fact]
    public async Task KeepBothCollision_CreatesRenamedCopy_AndReportsActualName()
    {
        var source = MakeSource("dup.txt", "new");
        File.WriteAllText(Path.Combine(_destDir, "dup.txt"), "old");
        var events = new CollectingEvents();

        var result = await _service.CopyAsync([source], _destDir, new FileOperationContext
        {
            Collision = CollisionOption.KeepBoth,
            Events = events,
        });

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(_destDir, "dup.txt")).Should().Be("old", "기존 파일은 보존");
        Directory.GetFiles(_destDir).Should().HaveCount(2, "새 이름으로 둘 다 유지");

        var reported = events.Completed.Should().ContainSingle().Subject;
        reported.NewPath.Should().NotBe(Path.Combine(_destDir, "dup.txt"), "실제 부여된 새 이름이 보고되어야 함");
        File.Exists(reported.NewPath!).Should().BeTrue();
    }

    [Fact]
    public async Task OverwriteCollision_ReplacesSilently()
    {
        var source = MakeSource("dup.txt", "new");
        File.WriteAllText(Path.Combine(_destDir, "dup.txt"), "old");

        var result = await _service.CopyAsync([source], _destDir, new FileOperationContext
        {
            Collision = CollisionOption.Overwrite,
        });

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(_destDir, "dup.txt")).Should().Be("new");
    }

    [Fact]
    public async Task PreCancelledControl_AbortsOperation()
    {
        var source = MakeSource("cancelme.txt");
        using var control = new OperationControl();
        control.Cancel();

        var result = await _service.CopyAsync([source], _destDir, new FileOperationContext
        {
            Control = control,
        });

        result.Aborted.Should().BeTrue();
        File.Exists(Path.Combine(_destDir, "cancelme.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteWithContext_ReportsRecycledItemPath()
    {
        var source = MakeSource("recycleme.txt");
        var events = new CollectingEvents();

        var result = await _service.DeleteAsync([source], permanent: false, new FileOperationContext
        {
            Events = events,
        });

        result.Succeeded.Should().BeTrue();
        File.Exists(source).Should().BeFalse();

        // 휴지통 물리 항목($R...) 경로가 보고되면 Undo(되돌려 이동)가 가능하다.
        // 일부 환경(휴지통 비활성 볼륨)에서는 영구 삭제로 폴백되어 NewPath가 없을 수 있다.
        var reported = events.Completed.Should().ContainSingle().Subject;
        reported.SourcePath.Should().Be(source);
    }
}

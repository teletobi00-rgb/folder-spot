using Explorer.Indexing.Usn;
using FluentAssertions;

namespace Explorer.Indexing.Tests.Usn;

public sealed class UsnChangeTrackerTests
{
    private const ulong RootFrn = 5;
    private const uint Create = 0x00000100;
    private const uint Delete = 0x00000200;
    private const uint RenameOld = 0x00001000;
    private const uint RenameNew = 0x00002000;
    private const uint DataExtend = 0x00000002;

    private static RawUsnRecord Rec(ulong frn, ulong parent, string name, uint reason, bool isDir = false) =>
        new(frn, parent, name, isDir, reason, Usn: 0);

    private static UsnChangeTracker NewTracker()
    {
        var tracker = new UsnChangeTracker(RootFrn, @"C:\");
        // 시드: C:\Work\ (dir 10), C:\Work\plan.txt (file 20)
        tracker.Seed(Rec(10, RootFrn, "Work", reason: 0, isDir: true));
        tracker.Seed(Rec(20, 10, "plan.txt", reason: 0));
        return tracker;
    }

    [Fact]
    public void Create_NewFileUnderKnownDir_EmitsCreatedWithFullPath()
    {
        var tracker = NewTracker();

        var changes = tracker.Process(Rec(30, 10, "new.txt", Create));

        changes.Should().ContainSingle();
        changes[0].Kind.Should().Be(FileChangeKind.Created);
        changes[0].FullPath.Should().Be(@"C:\Work\new.txt");
    }

    [Fact]
    public void Delete_KnownFile_EmitsDeletedWithLastKnownPath()
    {
        var tracker = NewTracker();

        var changes = tracker.Process(Rec(20, 10, "plan.txt", Delete));

        changes.Should().ContainSingle();
        changes[0].Kind.Should().Be(FileChangeKind.Deleted);
        changes[0].FullPath.Should().Be(@"C:\Work\plan.txt");
    }

    [Fact]
    public void Rename_OldThenNew_SameDir_EmitsRenamed()
    {
        var tracker = NewTracker();

        tracker.Process(Rec(20, 10, "plan.txt", RenameOld)).Should().BeEmpty("OLD만으로는 미발생");
        var changes = tracker.Process(Rec(20, 10, "plan-v2.txt", RenameNew));

        changes.Should().ContainSingle();
        changes[0].Kind.Should().Be(FileChangeKind.Renamed);
        changes[0].OldFullPath.Should().Be(@"C:\Work\plan.txt");
        changes[0].FullPath.Should().Be(@"C:\Work\plan-v2.txt");
    }

    [Fact]
    public void Rename_MoveToDifferentDir_TracksBothPaths()
    {
        var tracker = NewTracker();
        tracker.Seed(Rec(11, RootFrn, "Archive", reason: 0, isDir: true)); // C:\Archive\

        tracker.Process(Rec(20, 10, "plan.txt", RenameOld));
        var changes = tracker.Process(Rec(20, 11, "plan.txt", RenameNew)); // Work → Archive 이동

        changes[0].Kind.Should().Be(FileChangeKind.Renamed);
        changes[0].OldFullPath.Should().Be(@"C:\Work\plan.txt");
        changes[0].FullPath.Should().Be(@"C:\Archive\plan.txt");
    }

    [Fact]
    public void RenameNew_WithoutPriorOld_TreatedAsCreate()
    {
        var tracker = NewTracker();

        var changes = tracker.Process(Rec(40, 10, "appeared.txt", RenameNew));

        changes[0].Kind.Should().Be(FileChangeKind.Created);
        changes[0].FullPath.Should().Be(@"C:\Work\appeared.txt");
    }

    [Fact]
    public void DataChange_EmitsModified()
    {
        var tracker = NewTracker();

        var changes = tracker.Process(Rec(20, 10, "plan.txt", DataExtend));

        changes[0].Kind.Should().Be(FileChangeKind.Modified);
        changes[0].FullPath.Should().Be(@"C:\Work\plan.txt");
    }

    [Fact]
    public void Create_UnderUnknownParent_EmitsNothing()
    {
        var tracker = NewTracker();

        var changes = tracker.Process(Rec(50, 999, "orphan.txt", Create));

        changes.Should().BeEmpty("부모를 해석할 수 없으면 델타 없음");
    }

    [Fact]
    public void CreateThenDelete_LeavesMapClean()
    {
        var tracker = NewTracker();

        tracker.Process(Rec(60, 10, "temp.txt", Create))[0].Kind.Should().Be(FileChangeKind.Created);
        var deleted = tracker.Process(Rec(60, 10, "temp.txt", Delete));

        deleted[0].Kind.Should().Be(FileChangeKind.Deleted);
        deleted[0].FullPath.Should().Be(@"C:\Work\temp.txt");
    }

    [Fact]
    public void RenameDirectory_SubsequentChildPathsUseNewName()
    {
        var tracker = NewTracker();

        // Work 디렉터리(10) 이름변경: Work → Projects
        tracker.Process(Rec(10, RootFrn, "Work", RenameOld));
        tracker.Process(Rec(10, RootFrn, "Projects", RenameNew));

        // 이제 자식 파일의 데이터 변경 → 새 디렉터리 경로로 해석돼야 함
        var changes = tracker.Process(Rec(20, 10, "plan.txt", DataExtend));
        changes[0].FullPath.Should().Be(@"C:\Projects\plan.txt");
    }
}

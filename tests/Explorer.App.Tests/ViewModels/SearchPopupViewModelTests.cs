using Explorer.App.Tests.TestSupport;
using Explorer.App.ViewModels;
using Explorer.Core.FileSystem;
using Explorer.Core.Search;
using Explorer.Indexing;
using Explorer.Indexing.Index;
using Explorer.Shell.Icons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.ViewModels;

public sealed class SearchPopupViewModelTests : IDisposable
{
    private readonly FileIndexCatalog _catalog = new();
    private readonly IFileLauncher _launcher = Substitute.For<IFileLauncher>();
    private readonly ISearchUsageStore _usage = Substitute.For<ISearchUsageStore>();

    public void Dispose() => _catalog.Dispose();

    public SearchPopupViewModelTests()
    {
        var index = _catalog.Current;
        index.AddOrUpdate(new IndexItem(@"C:\문서", "회의록.hwp", false, 100, 0));
        index.AddOrUpdate(new IndexItem(@"C:\문서", "회의자료", true, 0, 0));
        index.AddOrUpdate(new IndexItem(@"C:\코드", "main.cs", false, 50, 0));
    }

    private SearchPopupViewModel CreateViewModel() => new(
        _catalog, _launcher, _usage, Substitute.For<IShellIconProvider>(),
        NullLogger<SearchPopupViewModel>.Instance);

    [Fact]
    public async Task TypingQuery_DebouncesThenShowsResults_FirstSelected()
    {
        var vm = CreateViewModel();

        vm.Query = "회의";

        await FileListTestContext.WaitUntilAsync(
            () => vm.Results.Count == 2, "디바운스 후 인덱스 결과가 표시");
        vm.Selected.Should().BeSameAs(vm.Results[0]);
        vm.IsSearching.Should().BeFalse();
    }

    [Fact]
    public async Task ClearingQuery_ClearsResults()
    {
        var vm = CreateViewModel();
        vm.Query = "main";
        await FileListTestContext.WaitUntilAsync(() => vm.Results.Count == 1, "검색 완료");

        vm.Query = "";

        await FileListTestContext.WaitUntilAsync(() => vm.Results.Count == 0, "빈 질의는 빈 결과");
        vm.Selected.Should().BeNull();
    }

    [Fact]
    public async Task MruUsage_BoostsWithinSameRank()
    {
        // 같은 부분일치 랭크의 두 항목 — 사용 빈도가 높은 쪽이 먼저 와야 한다.
        _catalog.Current.AddOrUpdate(new IndexItem(@"C:\a", "보고서-알파.docx", false, 1, 0));
        _catalog.Current.AddOrUpdate(new IndexItem(@"C:\a", "보고서-베타.docx", false, 1, 0));
        _usage.GetCount(@"C:\a\보고서-베타.docx").Returns(5);
        var vm = CreateViewModel();

        vm.Query = "보고서-";

        await FileListTestContext.WaitUntilAsync(() => vm.Results.Count == 2, "검색 완료");
        vm.Results[0].Name.Should().Be("보고서-베타.docx", "사용 빈도 가중");
    }

    [Fact]
    public async Task OpenSelected_File_LaunchesAndRecordsUsage_AndHides()
    {
        var vm = CreateViewModel();
        vm.Query = "main";
        await FileListTestContext.WaitUntilAsync(() => vm.Results.Count == 1, "검색 완료");
        var hidden = false;
        vm.HideRequested += (_, _) => hidden = true;

        vm.OpenSelectedCommand.Execute(null);

        _launcher.Received(1).Launch(@"C:\코드\main.cs");
        _usage.Received(1).Record(@"C:\코드\main.cs");
        hidden.Should().BeTrue();
    }

    [Fact]
    public async Task OpenSelected_Directory_RaisesOpenFolder()
    {
        var vm = CreateViewModel();
        vm.Query = "회의자료";
        await FileListTestContext.WaitUntilAsync(() => vm.Results.Count == 1, "검색 완료");
        string? opened = null;
        vm.OpenFolderRequested += (_, path) => opened = path;

        vm.OpenSelectedCommand.Execute(null);

        opened.Should().Be(@"C:\문서\회의자료");
        _launcher.DidNotReceiveWithAnyArgs().Launch(default!);
    }

    [Fact]
    public async Task RevealSelected_RaisesDirectoryAndPath()
    {
        var vm = CreateViewModel();
        vm.Query = "회의록";
        await FileListTestContext.WaitUntilAsync(() => vm.Results.Count == 1, "검색 완료");
        (string Directory, string FullPath)? revealed = null;
        vm.RevealRequested += (_, target) => revealed = target;

        vm.RevealSelectedCommand.Execute(null);

        revealed.Should().NotBeNull();
        revealed!.Value.Directory.Should().Be(@"C:\문서");
        revealed.Value.FullPath.Should().Be(@"C:\문서\회의록.hwp");
    }

    [Fact]
    public async Task MoveSelection_ClampsAtBounds()
    {
        var vm = CreateViewModel();
        vm.Query = "회의";
        await FileListTestContext.WaitUntilAsync(() => vm.Results.Count == 2, "검색 완료");

        vm.MoveSelection(+1);
        vm.Selected.Should().BeSameAs(vm.Results[1]);

        vm.MoveSelection(+1);
        vm.Selected.Should().BeSameAs(vm.Results[1], "끝에서 더 내려가지 않음");

        vm.MoveSelection(-1);
        vm.MoveSelection(-1);
        vm.Selected.Should().BeSameAs(vm.Results[0], "처음에서 더 올라가지 않음");
    }

    [Fact]
    public async Task RapidTyping_OnlyLastQueryWins()
    {
        var vm = CreateViewModel();

        vm.Query = "회";
        vm.Query = "회의";
        vm.Query = "main";

        await FileListTestContext.WaitUntilAsync(
            () => vm.Results.Count == 1 && vm.Results[0].Name == "main.cs",
            "마지막 질의 결과만 반영");
    }
}

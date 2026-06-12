using Explorer.App.ViewModels;
using Explorer.Core.Favorites;
using Explorer.Core.FileSystem;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explorer.App.Tests.ViewModels;

public sealed class FavoritesViewModelTests
{
    private readonly IFavoritesService _service = Substitute.For<IFavoritesService>();
    private readonly IFileLauncher _launcher = Substitute.For<IFileLauncher>();

    private FavoritesViewModel CreateViewModel() =>
        new(_service, _launcher, NullLogger<FavoritesViewModel>.Instance);

    private void SetServiceItems(params FavoriteItem[] items) =>
        _service.Items.Returns(items);

    [Fact]
    public void ChangedEvent_RebuildsItems()
    {
        var vm = CreateViewModel();
        vm.Items.Should().BeEmpty();

        SetServiceItems(new FavoriteItem { Path = @"C:\Work", IsDirectory = true });
        _service.Changed += Raise.Event<EventHandler>(_service, EventArgs.Empty);

        vm.Items.Should().ContainSingle(i => i.Path == @"C:\Work" && i.IsDirectory);
        vm.Items[0].DisplayName.Should().Be("Work");
    }

    [Fact]
    public void DisplayName_UsesLabelWhenPresent_AndRootPathFallback()
    {
        SetServiceItems(
            new FavoriteItem { Path = @"C:\Work", IsDirectory = true, Label = "작업폴더" },
            new FavoriteItem { Path = @"C:\", IsDirectory = true });
        _service.Changed += Raise.Event<EventHandler>(_service, EventArgs.Empty);
        var vm = CreateViewModel();

        _service.Changed += Raise.Event<EventHandler>(_service, EventArgs.Empty);

        vm.Items[0].DisplayName.Should().Be("작업폴더");
        vm.Items[1].DisplayName.Should().Be(@"C:\");
    }

    [Fact]
    public void Open_Folder_RaisesFolderOpenRequested()
    {
        SetServiceItems(new FavoriteItem { Path = @"C:\Work", IsDirectory = true });
        var vm = CreateViewModel();
        _service.Changed += Raise.Event<EventHandler>(_service, EventArgs.Empty);
        string? opened = null;
        vm.FolderOpenRequested += (_, path) => opened = path;

        vm.OpenCommand.Execute(vm.Items[0]);

        opened.Should().Be(@"C:\Work");
        _launcher.DidNotReceiveWithAnyArgs().Launch(default!);
    }

    [Fact]
    public void Open_File_LaunchesIt()
    {
        SetServiceItems(new FavoriteItem { Path = @"C:\notes.txt", IsDirectory = false });
        var vm = CreateViewModel();
        _service.Changed += Raise.Event<EventHandler>(_service, EventArgs.Empty);

        vm.OpenCommand.Execute(vm.Items[0]);

        _launcher.Received(1).Launch(@"C:\notes.txt");
    }

    [Fact]
    public void Open_LaunchFailure_IsSwallowed()
    {
        SetServiceItems(new FavoriteItem { Path = @"C:\gone.txt", IsDirectory = false });
        _launcher.When(l => l.Launch(Arg.Any<string>())).Throw(new InvalidOperationException());
        var vm = CreateViewModel();
        _service.Changed += Raise.Event<EventHandler>(_service, EventArgs.Empty);

        var act = () => vm.OpenCommand.Execute(vm.Items[0]);

        act.Should().NotThrow();
    }

    [Fact]
    public void Remove_DelegatesToService()
    {
        SetServiceItems(new FavoriteItem { Path = @"C:\Work", IsDirectory = true });
        var vm = CreateViewModel();
        _service.Changed += Raise.Event<EventHandler>(_service, EventArgs.Empty);

        vm.RemoveCommand.Execute(vm.Items[0]);

        _service.Received(1).Remove(@"C:\Work");
    }

    [Fact]
    public void MoveUpAndDown_UseCurrentIndex()
    {
        SetServiceItems(
            new FavoriteItem { Path = @"C:\a", IsDirectory = true },
            new FavoriteItem { Path = @"C:\b", IsDirectory = true });
        var vm = CreateViewModel();
        _service.Changed += Raise.Event<EventHandler>(_service, EventArgs.Empty);

        vm.MoveUpCommand.Execute(vm.Items[1]);
        _service.Received(1).Move(@"C:\b", 0);

        vm.MoveDownCommand.Execute(vm.Items[0]);
        _service.Received(1).Move(@"C:\a", 1);
    }

    [Fact]
    public void AddPaths_SkipsInvalidEntries()
    {
        _service.When(s => s.Add("bad-relative", Arg.Any<bool?>())).Throw(new ArgumentException("bad"));
        var vm = CreateViewModel();

        var act = () => vm.AddPaths([@"C:\ok", "bad-relative"]);

        act.Should().NotThrow();
        _service.Received(1).Add(@"C:\ok");
    }
}

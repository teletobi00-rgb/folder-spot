using Explorer.App.ViewModels;
using FluentAssertions;

namespace Explorer.App.Tests.ViewModels;

public sealed class AddressBarViewModelTests
{
    [Fact]
    public void Submit_ValidPath_RaisesNormalizedNavigationRequest()
    {
        var vm = new AddressBarViewModel { Text = @"  C:\Users\  " };
        string? requested = null;
        vm.NavigationRequested += (_, path) => requested = path;

        vm.SubmitCommand.Execute(null);

        requested.Should().Be(@"C:\Users");
        vm.ErrorText.Should().BeNull();
    }

    [Fact]
    public void Submit_RelativePath_SetsErrorAndDoesNotRaise()
    {
        var vm = new AddressBarViewModel { Text = @"relative\path" };
        var raised = false;
        vm.NavigationRequested += (_, _) => raised = true;

        vm.SubmitCommand.Execute(null);

        raised.Should().BeFalse();
        vm.ErrorText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Submit_EmptyText_DoesNothing()
    {
        var vm = new AddressBarViewModel { Text = "   " };
        var raised = false;
        vm.NavigationRequested += (_, _) => raised = true;

        vm.SubmitCommand.Execute(null);

        raised.Should().BeFalse();
        vm.ErrorText.Should().BeNull();
    }

    [Fact]
    public void SetCurrentPath_SyncsTextAndClearsError()
    {
        var vm = new AddressBarViewModel { Text = "bad" };
        vm.SubmitCommand.Execute(null);
        vm.ErrorText.Should().NotBeNull();

        vm.SetCurrentPath(@"C:\Users");

        vm.Text.Should().Be(@"C:\Users");
        vm.ErrorText.Should().BeNull();
    }
}

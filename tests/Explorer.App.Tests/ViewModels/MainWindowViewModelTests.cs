using Explorer.App.Services;
using Explorer.App.ViewModels;
using Explorer.Core.Settings;
using FluentAssertions;
using NSubstitute;

namespace Explorer.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IThemeService _themeService = Substitute.For<IThemeService>();

    private MainWindowViewModel CreateViewModel(AppTheme initialTheme)
    {
        _settings.Current.Returns(new AppSettings { Theme = initialTheme });
        _settings.Update(Arg.Any<Func<AppSettings, AppSettings>>())
            .Returns(call => call.Arg<Func<AppSettings, AppSettings>>()(_settings.Current));
        return new MainWindowViewModel(_settings, _themeService);
    }

    [Fact]
    public void Ctor_TakesInitialThemeFromSettings()
    {
        var viewModel = CreateViewModel(AppTheme.Dark);

        viewModel.CurrentTheme.Should().Be(AppTheme.Dark);
    }

    [Theory]
    [InlineData(AppTheme.System, AppTheme.Light)]
    [InlineData(AppTheme.Light, AppTheme.Dark)]
    [InlineData(AppTheme.Dark, AppTheme.System)]
    public void ToggleTheme_CyclesTheme_AppliesAndPersists(AppTheme from, AppTheme expected)
    {
        var viewModel = CreateViewModel(from);

        viewModel.ToggleThemeCommand.Execute(null);

        viewModel.CurrentTheme.Should().Be(expected);
        _themeService.Received(1).Apply(expected);
        _settings.Received(1).Update(Arg.Any<Func<AppSettings, AppSettings>>());
    }
}

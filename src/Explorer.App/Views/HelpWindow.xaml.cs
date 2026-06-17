using System.Windows;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

public partial class HelpWindow : FluentWindow
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

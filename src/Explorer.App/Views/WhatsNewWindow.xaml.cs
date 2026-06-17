using System.Collections.Generic;
using System.Windows;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

public partial class WhatsNewWindow : FluentWindow
{
    public WhatsNewWindow(string version, IReadOnlyList<string> notes)
    {
        InitializeComponent();
        HeaderText.Text = $"Folder Spot {version} — 새 기능";
        NotesList.ItemsSource = notes;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

using System.Globalization;
using System.Windows;
using Explorer.Core.FileOperations;
using Explorer.Core.Formatting;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

public partial class ConflictDialog : FluentWindow
{
    public ConflictDialog(FileConflict conflict, int remainingCount)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        InitializeComponent();

        NameText.Text = conflict.Name;
        RemainingText.Text = remainingCount > 0 ? $"이 항목 외 {remainingCount}개의 충돌이 더 있습니다" : string.Empty;
        RemainingText.Visibility = remainingCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyToAllCheck.Visibility = remainingCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        SourceText.Text = Describe(conflict.IsDirectory, conflict.SourceSize, conflict.SourceModified);
        TargetText.Text = Describe(conflict.IsDirectory, conflict.TargetSize, conflict.TargetModified);
    }

    public ConflictDecision? Decision { get; private set; }

    public bool ApplyToAll => ApplyToAllCheck.IsChecked == true;

    private static string Describe(bool isDirectory, long size, DateTime modified)
    {
        var sizeText = isDirectory ? "폴더" : FileSizeFormatter.Format(size);
        var dateText = modified == default
            ? string.Empty
            : " · " + modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        return sizeText + dateText;
    }

    private void OnOverwrite(object sender, RoutedEventArgs e) => Close(ConflictDecision.Overwrite);

    private void OnKeepBoth(object sender, RoutedEventArgs e) => Close(ConflictDecision.KeepBoth);

    private void OnSkip(object sender, RoutedEventArgs e) => Close(ConflictDecision.Skip);

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Decision = null;
        DialogResult = false;
    }

    private void Close(ConflictDecision decision)
    {
        Decision = decision;
        DialogResult = true;
    }
}

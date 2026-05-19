using SmartViews.Engine;
using System.Windows;

namespace SmartViews.UI;

public partial class PreflightDialog : Window
{
    public PreflightDialog(IReadOnlyList<PreflightIssue> issues)
    {
        InitializeComponent();
        LvIssues.ItemsSource = issues;
    }

    private void Proceed_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

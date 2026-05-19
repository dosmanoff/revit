using SmartViews.Engine;
using System.Windows;

namespace SmartViews.UI;

public partial class ErrorSummaryDialog : Window
{
    public ErrorSummaryDialog(ViewCreationResult result)
    {
        InitializeComponent();
        Populate(result);
    }

    private void Populate(ViewCreationResult result)
    {
        TxtCreated.Text = result.CreatedCount.ToString();
        TxtSkipped.Text = result.SkippedCount.ToString();
        TxtErrors.Text  = result.ErrorCount.ToString();

        if (result.HasErrors)
        {
            PnlErrors.Visibility = Visibility.Visible;
            LvErrors.ItemsSource = result.Errors;

            TxtPrompt.Text = result.CreatedCount > 0
                ? $"{result.ErrorCount} error(s) occurred. Commit the {result.CreatedCount} view(s) that succeeded, or roll back everything?"
                : $"{result.ErrorCount} error(s) occurred and no views were created.";

            BtnCommit.IsEnabled = result.CreatedCount > 0;
        }
        else
        {
            TxtPrompt.Text = result.CreatedCount > 0
                ? $"All {result.CreatedCount} view(s) created successfully. Commit to the model?"
                : "No views were created.";

            BtnCommit.IsEnabled = result.CreatedCount > 0;
        }
    }

    private void Commit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void RollBack_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace RevitActionRecorder.Application;

/// <summary>Диалог комментария для Mark moment.</summary>
internal sealed class MarkMomentWindow : Window
{
    private readonly TextBox _text;

    public string? CommentText { get; private set; }

    public MarkMomentWindow(IntPtr ownerHandle)
    {
        Title = "Mark moment — важный момент";
        Width = 520;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _text = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Отмена", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            CommentText = _text.Text;
            DialogResult = true;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Комментарий (что здесь важного, какое решение принято):",
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(label, 0);
        Grid.SetRow(_text, 1);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(label);
        grid.Children.Add(_text);
        grid.Children.Add(buttons);
        Content = grid;

        if (ownerHandle != IntPtr.Zero)
            new WindowInteropHelper(this) { Owner = ownerHandle };

        Loaded += (_, _) => _text.Focus();
    }
}

using System.Windows;
using System.Windows.Controls;
using SlabReinforcement.Config;

namespace SlabReinforcement.UI;

/// <summary>Compact code-only dialog for Slab Views: plan scale, layer isolation, schedule/sheet toggles.</summary>
public sealed class SlabViewsDialog : Window
{
    private readonly SlabViewsConfig _cfg;
    private readonly TextBox _scale;
    private readonly ComboBox _isolation;
    private readonly CheckBox _schedule;
    private readonly CheckBox _sheet;

    public SlabViewsDialog(SlabViewsConfig cfg)
    {
        _cfg = cfg;
        Title = "Slab Views";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        _scale = new TextBox { Text = cfg.PlanScale.ToString() };
        _isolation = new ComboBox();
        foreach (string v in Enum.GetNames<LayerIsolation>()) _isolation.Items.Add(v);
        _isolation.SelectedItem = cfg.Isolation.ToString();
        _schedule = new CheckBox { Content = "Create rebar schedule", IsChecked = cfg.CreateSchedule, Margin = new Thickness(0, 6, 0, 2) };
        _sheet = new CheckBox { Content = "Place views on a sheet", IsChecked = cfg.PlaceOnSheet, Margin = new Thickness(0, 2, 0, 6) };

        var ok = new Button { Content = "Create views", Width = 110, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { WriteBack(); DialogResult = true; };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(Row("Plan scale (1:n):", _scale));
        root.Children.Add(Row("Layer isolation:", _isolation));
        root.Children.Add(_schedule);
        root.Children.Add(_sheet);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
    }

    private void WriteBack()
    {
        if (int.TryParse(_scale.Text.Trim(), out int s) && s > 0) _cfg.PlanScale = s;
        if (_isolation.SelectedItem is string iso && Enum.TryParse(iso, out LayerIsolation li)) _cfg.Isolation = li;
        _cfg.CreateSchedule = _schedule.IsChecked == true;
        _cfg.PlaceOnSheet = _sheet.IsChecked == true;
    }

    private static FrameworkElement Row(string label, FrameworkElement field)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        p.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
        field.Width = 220;
        p.Children.Add(field);
        return p;
    }
}

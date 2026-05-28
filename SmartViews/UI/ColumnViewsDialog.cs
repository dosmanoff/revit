using SmartViews.Config;
using System.Windows;
using System.Windows.Controls;
// Disambiguate WPF controls/enums from their System.Windows.Forms namesakes
// (this project enables both UseWPF and UseWindowsForms).
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace SmartViews.UI;

/// <summary>
/// Code-only options dialog for the Column Views tool. Surfaces the main settings —
/// naming templates, foreign-rebar treatment, and the schedule/sheet toggles — and
/// writes them back into <see cref="Config"/> on OK.
/// </summary>
public sealed class ColumnViewsDialog : Window
{
    private readonly ComboBox _foreignRebar;
    private readonly TextBox _elevationName;
    private readonly TextBox _planName;
    private readonly TextBox _rebarScheduleName;
    private readonly TextBox _bendingScheduleName;
    private readonly TextBox _sheetNumber;
    private readonly TextBox _sheetName;
    private readonly TextBox _titleBlock;
    private readonly CheckBox _createRebarSchedule;
    private readonly CheckBox _createBendingSchedule;
    private readonly CheckBox _placeOnSheet;

    public ColumnViewsConfig Config { get; }

    public ColumnViewsDialog(ColumnViewsConfig config)
    {
        Config = config;

        Title = "Column Views";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(SectionHeader("Naming templates  (tokens: {Mark} {Direction} {End} {Level})"));
        _elevationName       = AddTextRow(root, "Elevation view", config.ElevationNameTemplate);
        _planName            = AddTextRow(root, "End plan view", config.PlanNameTemplate);
        _rebarScheduleName   = AddTextRow(root, "Rebar schedule", config.RebarScheduleNameTemplate);
        _bendingScheduleName = AddTextRow(root, "Bending schedule", config.BendingScheduleNameTemplate);
        _sheetNumber         = AddTextRow(root, "Sheet number", config.SheetNumberTemplate);
        _sheetName           = AddTextRow(root, "Sheet name", config.SheetNameTemplate);

        root.Children.Add(SectionHeader("Options"));
        _foreignRebar = AddComboRow(root, "Rebar from other columns",
            new[] { "Hide", "Halftone", "Show" }, config.ForeignRebar.ToString());
        _titleBlock = AddTextRow(root, "Title block (blank = first available)", config.TitleBlockName ?? string.Empty);

        _createRebarSchedule   = AddCheckRow(root, "Create rebar schedule", config.CreateRebarSchedule);
        _createBendingSchedule = AddCheckRow(root, "Create bending-detail schedule", config.CreateBendingSchedule);
        _placeOnSheet          = AddCheckRow(root, "Place views and schedules on a sheet", config.PlaceOnSheet);

        root.Children.Add(BuildButtonRow());

        Content = root;
    }

    // -----------------------------------------------------------------------

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Config.ElevationNameTemplate       = _elevationName.Text.Trim();
        Config.PlanNameTemplate            = _planName.Text.Trim();
        Config.RebarScheduleNameTemplate   = _rebarScheduleName.Text.Trim();
        Config.BendingScheduleNameTemplate = _bendingScheduleName.Text.Trim();
        Config.SheetNumberTemplate         = _sheetNumber.Text.Trim();
        Config.SheetNameTemplate           = _sheetName.Text.Trim();

        string titleBlock = _titleBlock.Text.Trim();
        Config.TitleBlockName = string.IsNullOrEmpty(titleBlock) ? null : titleBlock;

        Config.ForeignRebar = Enum.TryParse((_foreignRebar.SelectedItem as string), out ForeignRebarMode mode)
            ? mode
            : ForeignRebarMode.Hide;

        Config.CreateRebarSchedule   = _createRebarSchedule.IsChecked == true;
        Config.CreateBendingSchedule = _createBendingSchedule.IsChecked == true;
        Config.PlaceOnSheet          = _placeOnSheet.IsChecked == true;

        DialogResult = true;
    }

    // -----------------------------------------------------------------------
    // Layout helpers
    // -----------------------------------------------------------------------

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 12, 0, 6),
    };

    private static TextBox AddTextRow(Panel parent, string label, string value)
    {
        parent.Children.Add(RowLabel(label));
        var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(2) };
        parent.Children.Add(box);
        return box;
    }

    private static ComboBox AddComboRow(Panel parent, string label, string[] items, string selected)
    {
        parent.Children.Add(RowLabel(label));
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (string item in items)
            combo.Items.Add(item);
        combo.SelectedItem = items.Contains(selected) ? selected : items[0];
        parent.Children.Add(combo);
        return combo;
    }

    private static CheckBox AddCheckRow(Panel parent, string label, bool isChecked)
    {
        var check = new CheckBox { Content = label, IsChecked = isChecked, Margin = new Thickness(0, 4, 0, 0) };
        parent.Children.Add(check);
        return check;
    }

    private static TextBlock RowLabel(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private StackPanel BuildButtonRow()
    {
        var ok = new Button { Content = "Run", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += Ok_Click;

        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { ok, cancel },
        };
    }
}

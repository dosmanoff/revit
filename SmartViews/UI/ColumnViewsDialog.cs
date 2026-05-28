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
using VerticalAlignment = System.Windows.VerticalAlignment;
using RvtDetailLevel = Autodesk.Revit.DB.ViewDetailLevel;
using RvtDisplayStyle = Autodesk.Revit.DB.DisplayStyle;

namespace SmartViews.UI;

/// <summary>
/// Code-only options dialog for the Column Views tool. Surfaces naming templates, view
/// appearance (scale / detail level / visual style), foreign-rebar treatment, and the
/// schedule/graphics/sheet toggles, writing them back into <see cref="Config"/> on OK.
/// </summary>
public sealed class ColumnViewsDialog : Window
{
    private const string FirstAvailable = "(first available)";

    private static readonly string[] DetailLevels = { "Coarse", "Medium", "Fine" };

    private static readonly (string Label, RvtDisplayStyle Style)[] VisualStyles =
    {
        ("Wireframe", RvtDisplayStyle.Wireframe),
        ("Hidden Line", RvtDisplayStyle.HLR),
        ("Shaded", RvtDisplayStyle.Shading),
        ("Shaded with Edges", RvtDisplayStyle.ShadingWithEdges),
    };

    private readonly TextBox _elevationName;
    private readonly TextBox _planName;
    private readonly TextBox _rebarScheduleName;
    private readonly TextBox _sheetNumber;
    private readonly TextBox _sheetName;
    private readonly ComboBox _titleBlock;
    private readonly ComboBox _foreignRebar;
    private readonly TextBox _viewScale;
    private readonly ComboBox _detailLevel;
    private readonly ComboBox _visualStyle;
    private readonly CheckBox _createRebarSchedule;
    private readonly CheckBox _bendingGraphics;
    private readonly CheckBox _create3D;
    private readonly CheckBox _placeOnSheet;

    private readonly int _columnCount;

    public ColumnViewsConfig Config { get; }

    /// <summary>True when the user asked to re-pick columns instead of running.</summary>
    public bool ReselectRequested { get; private set; }

    public ColumnViewsDialog(ColumnViewsConfig config, IReadOnlyList<string> titleBlockNames, int columnCount)
    {
        Config = config;
        _columnCount = columnCount;

        Title = "Column Views";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(BuildSelectionRow(columnCount));

        root.Children.Add(SectionHeader("Naming templates  (tokens: {Mark} {Direction} {End} {Level})"));
        _elevationName     = AddTextRow(root, "Elevation view", config.ElevationNameTemplate);
        _planName          = AddTextRow(root, "End plan view", config.PlanNameTemplate);
        _rebarScheduleName = AddTextRow(root, "Rebar schedule", config.RebarScheduleNameTemplate);
        _sheetNumber       = AddTextRow(root, "Sheet number", config.SheetNumberTemplate);
        _sheetName         = AddTextRow(root, "Sheet name", config.SheetNameTemplate);

        root.Children.Add(SectionHeader("View appearance"));
        _viewScale   = AddTextRow(root, "Scale denominator  (12 = 1\"=1'-0\", 48 = 1/4\"=1'-0\")",
            config.ViewScale.ToString());
        _detailLevel = AddComboRow(root, "Detail level", DetailLevels, config.DetailLevel.ToString());
        _visualStyle = AddComboRow(root, "Visual style",
            VisualStyles.Select(v => v.Label).ToArray(), LabelFor(config.VisualStyle));

        root.Children.Add(SectionHeader("Options"));
        _foreignRebar = AddComboRow(root, "Rebar from other columns",
            new[] { "Hide", "Halftone", "Show" }, config.ForeignRebar.ToString());

        string[] titleBlockItems = new[] { FirstAvailable }.Concat(titleBlockNames).ToArray();
        _titleBlock = AddComboRow(root, "Title block",
            titleBlockItems, config.TitleBlockName ?? FirstAvailable);

        _createRebarSchedule = AddCheckRow(root, "Create rebar schedule", config.CreateRebarSchedule);
        _bendingGraphics     = AddCheckRow(root, "Generate bending-detail graphics (Shape Image column)", config.BendingDetailGraphics);
        _create3D            = AddCheckRow(root, "Add 3D view (column + its rebar only)", config.Create3DView);
        _placeOnSheet        = AddCheckRow(root, "Place views and schedule on a sheet", config.PlaceOnSheet);

        root.Children.Add(BuildButtonRow());

        Content = root;
    }

    // -----------------------------------------------------------------------

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Config.ElevationNameTemplate     = _elevationName.Text.Trim();
        Config.PlanNameTemplate          = _planName.Text.Trim();
        Config.RebarScheduleNameTemplate = _rebarScheduleName.Text.Trim();
        Config.SheetNumberTemplate       = _sheetNumber.Text.Trim();
        Config.SheetNameTemplate         = _sheetName.Text.Trim();

        string? titleBlock = _titleBlock.SelectedItem as string;
        Config.TitleBlockName =
            string.IsNullOrEmpty(titleBlock) || titleBlock == FirstAvailable ? null : titleBlock;

        Config.ForeignRebar = Enum.TryParse(_foreignRebar.SelectedItem as string, out ForeignRebarMode mode)
            ? mode
            : ForeignRebarMode.Hide;

        if (int.TryParse(_viewScale.Text.Trim(), out int scale) && scale > 0)
            Config.ViewScale = scale;

        if (Enum.TryParse(_detailLevel.SelectedItem as string, out RvtDetailLevel detail))
            Config.DetailLevel = detail;

        Config.VisualStyle = StyleFor(_visualStyle.SelectedItem as string);

        Config.CreateRebarSchedule   = _createRebarSchedule.IsChecked == true;
        Config.BendingDetailGraphics = _bendingGraphics.IsChecked == true;
        Config.Create3DView          = _create3D.IsChecked == true;
        Config.PlaceOnSheet          = _placeOnSheet.IsChecked == true;

        DialogResult = true;
    }

    private void Reselect_Click(object sender, RoutedEventArgs e)
    {
        ReselectRequested = true;
        DialogResult = true;
    }

    private static string LabelFor(RvtDisplayStyle style) =>
        VisualStyles.FirstOrDefault(v => v.Style == style).Label ?? "Hidden Line";

    private static RvtDisplayStyle StyleFor(string? label)
    {
        foreach ((string Label, RvtDisplayStyle Style) v in VisualStyles)
            if (string.Equals(v.Label, label, StringComparison.Ordinal))
                return v.Style;
        return RvtDisplayStyle.HLR;
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

    private StackPanel BuildSelectionRow(int columnCount)
    {
        var label = new TextBlock
        {
            Text = columnCount == 1 ? "1 column selected" : $"{columnCount} columns selected",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var select = new Button { Content = "Select columns…", Width = 130 };
        select.Click += Reselect_Click;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { label, select },
        };
    }

    private StackPanel BuildButtonRow()
    {
        var ok = new Button
        {
            Content = "Run",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            IsEnabled = _columnCount > 0,
        };
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

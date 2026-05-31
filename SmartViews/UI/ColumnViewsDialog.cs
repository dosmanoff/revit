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
/// appearance (per-view scale / detail level / visual style), schedule template, foreign-rebar
/// treatment, and the schedule/graphics/3D/bending/sheet toggles. Writes choices into
/// <see cref="Config"/> on OK.
/// </summary>
public sealed class ColumnViewsDialog : Window
{
    private const string FirstAvailable = "(first available)";
    private const string BuildFromScratch = "(build from scratch)";

    private static readonly string[] DetailLevels = { "Coarse", "Medium", "Fine" };

    private static readonly (string Label, RvtDisplayStyle Style)[] VisualStyles =
    {
        ("Wireframe", RvtDisplayStyle.Wireframe),
        ("Hidden Line", RvtDisplayStyle.HLR),
        ("Shaded", RvtDisplayStyle.Shading),
        ("Shaded with Edges", RvtDisplayStyle.ShadingWithEdges),
    };

    /// <summary>Standard architectural scales, label ↔ Revit scale denominator.</summary>
    private static readonly (string Label, int Value)[] Scales =
    {
        ("3\"=1'-0\" (4)",        4),
        ("1 1/2\"=1'-0\" (8)",    8),
        ("1\"=1'-0\" (12)",      12),
        ("3/4\"=1'-0\" (16)",    16),
        ("1/2\"=1'-0\" (24)",    24),
        ("3/8\"=1'-0\" (32)",    32),
        ("1/4\"=1'-0\" (48)",    48),
        ("3/16\"=1'-0\" (64)",   64),
        ("1/8\"=1'-0\" (96)",    96),
        ("1/16\"=1'-0\" (192)", 192),
    };

    private readonly TextBox _elevationName;
    private readonly TextBox _planName;
    private readonly TextBox _rebarScheduleName;
    private readonly TextBox _view3DName;
    private readonly TextBox _bendingDetailName;
    private readonly TextBox _sheetNumber;
    private readonly TextBox _sheetName;
    private readonly ComboBox _titleBlock;
    private readonly ComboBox _scheduleTemplate;
    private readonly ComboBox _foreignRebar;
    private readonly ComboBox _elevationScale;
    private readonly ComboBox _planScale;
    private readonly ComboBox _view3DScale;
    private readonly ComboBox _bendingDetailScale;
    private readonly ComboBox _detailLevel;
    private readonly ComboBox _visualStyle;
    private readonly CheckBox _createRebarSchedule;
    private readonly CheckBox _bendingGraphics;
    private readonly CheckBox _create3D;
    private readonly CheckBox _createBendingDetailView;
    private readonly CheckBox _placeOnSheet;

    private readonly int _columnCount;

    public ColumnViewsConfig Config { get; }

    /// <summary>True when the user asked to re-pick columns instead of running.</summary>
    public bool ReselectRequested { get; private set; }

    public ColumnViewsDialog(
        ColumnViewsConfig config,
        IReadOnlyList<string> titleBlockNames,
        IReadOnlyList<string> scheduleNames,
        int columnCount)
    {
        Config = config;
        _columnCount = columnCount;

        Title = "Column Views";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(BuildSelectionRow(columnCount));

        root.Children.Add(SectionHeader("Naming templates  (tokens: {Mark} {Direction} {End} {Level})"));
        _elevationName     = AddTextRow(root, "Elevation view", config.ElevationNameTemplate);
        _planName          = AddTextRow(root, "End plan view", config.PlanNameTemplate);
        _rebarScheduleName = AddTextRow(root, "Rebar schedule", config.RebarScheduleNameTemplate);
        _view3DName        = AddTextRow(root, "3D view", config.View3DNameTemplate);
        _bendingDetailName = AddTextRow(root, "Bending-detail view", config.BendingDetailViewNameTemplate);
        _sheetNumber       = AddTextRow(root, "Sheet number", config.SheetNumberTemplate);
        _sheetName         = AddTextRow(root, "Sheet name", config.SheetNameTemplate);

        root.Children.Add(SectionHeader("View appearance"));
        string[] scaleLabels = Scales.Select(s => s.Label).ToArray();
        _elevationScale     = AddComboRow(root, "Elevation scale", scaleLabels, ScaleLabel(config.ElevationScale));
        _planScale          = AddComboRow(root, "End plan scale", scaleLabels, ScaleLabel(config.PlanScale));
        _view3DScale        = AddComboRow(root, "3D view scale", scaleLabels, ScaleLabel(config.View3DScale));
        _bendingDetailScale = AddComboRow(root, "Bending-detail view scale", scaleLabels, ScaleLabel(config.BendingDetailScale));
        _detailLevel        = AddComboRow(root, "Detail level", DetailLevels, config.DetailLevel.ToString());
        _visualStyle        = AddComboRow(root, "Visual style",
            VisualStyles.Select(v => v.Label).ToArray(), VisualStyleLabel(config.VisualStyle));

        root.Children.Add(SectionHeader("Options"));
        _foreignRebar = AddComboRow(root, "Rebar from other columns",
            new[] { "Hide", "Halftone", "Show" }, config.ForeignRebar.ToString());

        string[] titleBlockItems = new[] { FirstAvailable }.Concat(titleBlockNames).ToArray();
        _titleBlock = AddComboRow(root, "Title block",
            titleBlockItems, config.TitleBlockName ?? FirstAvailable);

        string[] scheduleItems = new[] { BuildFromScratch }.Concat(scheduleNames).ToArray();
        _scheduleTemplate = AddComboRow(root, "Rebar schedule template",
            scheduleItems, config.ScheduleTemplateName ?? BuildFromScratch);

        _createRebarSchedule     = AddCheckRow(root, "Create rebar schedule", config.CreateRebarSchedule);
        _bendingGraphics         = AddCheckRow(root, "Include Shape Image column in built-from-scratch schedule", config.BendingDetailGraphics);
        _create3D                = AddCheckRow(root, "Add 3D view (column + its rebar only)", config.Create3DView);
        _createBendingDetailView = AddCheckRow(root, "Add bending-detail drafting view", config.CreateBendingDetailView);
        _placeOnSheet            = AddCheckRow(root, "Place views and schedule on a sheet", config.PlaceOnSheet);

        root.Children.Add(BuildButtonRow());

        Content = root;
    }

    // -----------------------------------------------------------------------

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Config.ElevationNameTemplate         = _elevationName.Text.Trim();
        Config.PlanNameTemplate              = _planName.Text.Trim();
        Config.RebarScheduleNameTemplate     = _rebarScheduleName.Text.Trim();
        Config.View3DNameTemplate            = _view3DName.Text.Trim();
        Config.BendingDetailViewNameTemplate = _bendingDetailName.Text.Trim();
        Config.SheetNumberTemplate           = _sheetNumber.Text.Trim();
        Config.SheetNameTemplate             = _sheetName.Text.Trim();

        string? titleBlock = _titleBlock.SelectedItem as string;
        Config.TitleBlockName =
            string.IsNullOrEmpty(titleBlock) || titleBlock == FirstAvailable ? null : titleBlock;

        string? scheduleTemplate = _scheduleTemplate.SelectedItem as string;
        Config.ScheduleTemplateName =
            string.IsNullOrEmpty(scheduleTemplate) || scheduleTemplate == BuildFromScratch ? null : scheduleTemplate;

        Config.ForeignRebar = Enum.TryParse(_foreignRebar.SelectedItem as string, out ForeignRebarMode mode)
            ? mode
            : ForeignRebarMode.Hide;

        Config.ElevationScale     = ScaleValue(_elevationScale.SelectedItem as string, Config.ElevationScale);
        Config.PlanScale          = ScaleValue(_planScale.SelectedItem as string, Config.PlanScale);
        Config.View3DScale        = ScaleValue(_view3DScale.SelectedItem as string, Config.View3DScale);
        Config.BendingDetailScale = ScaleValue(_bendingDetailScale.SelectedItem as string, Config.BendingDetailScale);

        if (Enum.TryParse(_detailLevel.SelectedItem as string, out RvtDetailLevel detail))
            Config.DetailLevel = detail;

        Config.VisualStyle = VisualStyleFor(_visualStyle.SelectedItem as string);

        Config.CreateRebarSchedule      = _createRebarSchedule.IsChecked == true;
        Config.BendingDetailGraphics    = _bendingGraphics.IsChecked == true;
        Config.Create3DView             = _create3D.IsChecked == true;
        Config.CreateBendingDetailView  = _createBendingDetailView.IsChecked == true;
        Config.PlaceOnSheet             = _placeOnSheet.IsChecked == true;

        DialogResult = true;
    }

    private void Reselect_Click(object sender, RoutedEventArgs e)
    {
        ReselectRequested = true;
        DialogResult = true;
    }

    // -----------------------------------------------------------------------
    // Scale / style label helpers
    // -----------------------------------------------------------------------

    private static string ScaleLabel(int value)
    {
        foreach ((string Label, int Value) s in Scales)
            if (s.Value == value) return s.Label;
        return Scales.First(s => s.Value == 12).Label; // fall back to 1"=1'-0"
    }

    private static int ScaleValue(string? label, int fallback)
    {
        foreach ((string Label, int Value) s in Scales)
            if (string.Equals(s.Label, label, StringComparison.Ordinal)) return s.Value;
        return fallback;
    }

    private static string VisualStyleLabel(RvtDisplayStyle style) =>
        VisualStyles.FirstOrDefault(v => v.Style == style).Label ?? "Hidden Line";

    private static RvtDisplayStyle VisualStyleFor(string? label)
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

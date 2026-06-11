using System.Windows;
using System.Windows.Controls;
using SlabReinforcement.Config;
using RvtDetailLevel = Autodesk.Revit.DB.ViewDetailLevel;
using RvtDisplayStyle = Autodesk.Revit.DB.DisplayStyle;

namespace SlabReinforcement.UI;

/// <summary>
/// Options dialog for Slab Views, at parity with SmartViews' ColumnViewsDialog: slab selection
/// with re-pick, naming templates, per-view scales (auto-fit or fixed), detail level / visual
/// style, layer isolation, title block / view template pickers and the section/3D/bending/
/// schedule/sheet toggles. Writes choices into <see cref="SlabViewsConfig"/> on Run.
/// </summary>
public sealed class SlabViewsDialog : Window
{
    private const string AutoFit = "Auto (fit to sheet)";
    private const string FirstAvailable = "(first available)";
    private const string NoTemplate = "(none)";

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
        ("1\"=1'-0\" (12)",      12),
        ("3/4\"=1'-0\" (16)",    16),
        ("1/2\"=1'-0\" (24)",    24),
        ("3/8\"=1'-0\" (32)",    32),
        ("1/4\"=1'-0\" (48)",    48),
        ("3/16\"=1'-0\" (64)",   64),
        ("1/8\"=1'-0\" (96)",    96),
        ("3/32\"=1'-0\" (128)", 128),
        ("1/16\"=1'-0\" (192)", 192),
    };

    private readonly SlabViewsConfig _cfg;
    private readonly int _slabCount;

    private readonly TextBox _layerName;
    private readonly TextBox _additionalName;
    private readonly TextBox _sectionName;
    private readonly TextBox _view3DName;
    private readonly TextBox _bendingName;
    private readonly TextBox _scheduleName;
    private readonly TextBox _sheetNumber;
    private readonly TextBox _sheetName;
    private readonly ComboBox _planScale;
    private readonly ComboBox _view3DScale;
    private readonly ComboBox _bendingScale;
    private readonly ComboBox _detailLevel;
    private readonly ComboBox _visualStyle;
    private readonly ComboBox _isolation;
    private readonly ComboBox _titleBlock;
    private readonly ComboBox _viewTemplate;
    private readonly CheckBox _middleBar;
    private readonly CheckBox _sections;
    private readonly CheckBox _create3D;
    private readonly CheckBox _bending;
    private readonly CheckBox _schedule;
    private readonly CheckBox _sheet;

    /// <summary>True when the user asked to re-pick slabs instead of running.</summary>
    public bool ReselectRequested { get; private set; }

    public SlabViewsDialog(
        SlabViewsConfig cfg,
        IReadOnlyList<string> titleBlockNames,
        IReadOnlyList<string> viewTemplateNames,
        int slabCount)
    {
        _cfg = cfg;
        _slabCount = slabCount;

        Title = "Slab Views";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(BuildSelectionRow(slabCount));

        root.Children.Add(SectionHeader("Naming templates  (tokens: {Mark} {N} {Layer} {Face} {Dir})"));
        _layerName      = AddTextRow(root, "Layer plan view", cfg.LayerViewNameTemplate);
        _additionalName = AddTextRow(root, "Additional-reinforcement view", cfg.AdditionalViewNameTemplate);
        _sectionName    = AddTextRow(root, "Section view", cfg.SectionNameTemplate);
        _view3DName     = AddTextRow(root, "3D view", cfg.View3DNameTemplate);
        _bendingName    = AddTextRow(root, "Bending-detail view", cfg.BendingDetailNameTemplate);
        _scheduleName   = AddTextRow(root, "Rebar schedule", cfg.ScheduleNameTemplate);
        _sheetNumber    = AddTextRow(root, "Sheet number", cfg.SheetNumberTemplate);
        _sheetName      = AddTextRow(root, "Sheet name", cfg.SheetNameTemplate);

        root.Children.Add(SectionHeader("View appearance"));
        string[] planScaleItems = new[] { AutoFit }.Concat(Scales.Select(s => s.Label)).ToArray();
        _planScale    = AddComboRow(root, "Plan / section scale",
            planScaleItems, cfg.AutoScale ? AutoFit : ScaleLabel(cfg.PlanScale));
        string[] scaleLabels = Scales.Select(s => s.Label).ToArray();
        _view3DScale  = AddComboRow(root, "3D view scale", scaleLabels, ScaleLabel(cfg.View3DScale));
        _bendingScale = AddComboRow(root, "Bending-detail view scale", scaleLabels, ScaleLabel(cfg.BendingDetailScale));
        _detailLevel  = AddComboRow(root, "Detail level", DetailLevels, cfg.DetailLevel.ToString());
        _visualStyle  = AddComboRow(root, "Visual style",
            VisualStyles.Select(v => v.Label).ToArray(), VisualStyleLabel(cfg.VisualStyle));

        root.Children.Add(SectionHeader("Options"));
        _isolation = AddComboRow(root, "Rebar from other layers",
            new[] { "Hide", "Halftone", "Show" }, cfg.Isolation.ToString());

        string[] titleBlockItems = new[] { FirstAvailable }.Concat(titleBlockNames).ToArray();
        _titleBlock = AddComboRow(root, "Title block", titleBlockItems, cfg.TitleBlockName ?? FirstAvailable);

        string[] templateItems = new[] { NoTemplate }.Concat(viewTemplateNames).ToArray();
        _viewTemplate = AddComboRow(root, "View template", templateItems, cfg.ViewTemplateName ?? NoTemplate);

        _middleBar = AddCheckRow(root, "Show rebar sets as the middle bar only", cfg.ShowMiddleBarOnly);
        _sections  = AddCheckRow(root, "Add cross-sections (A-A / B-B)", cfg.CreateSections);
        _create3D  = AddCheckRow(root, "Add 3D view (slab + its rebar only)", cfg.Create3DView);
        _bending   = AddCheckRow(root, "Add bending-detail drafting view", cfg.CreateBendingDetails);
        _schedule  = AddCheckRow(root, "Create per-layer rebar schedules", cfg.CreateSchedule);
        _sheet     = AddCheckRow(root, "Place views and schedules on a sheet", cfg.PlaceOnSheet);

        root.Children.Add(BuildButtonRow());

        Content = root;
    }

    // -----------------------------------------------------------------------

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _cfg.LayerViewNameTemplate      = _layerName.Text.Trim();
        _cfg.AdditionalViewNameTemplate = _additionalName.Text.Trim();
        _cfg.SectionNameTemplate        = _sectionName.Text.Trim();
        _cfg.View3DNameTemplate         = _view3DName.Text.Trim();
        _cfg.BendingDetailNameTemplate  = _bendingName.Text.Trim();
        _cfg.ScheduleNameTemplate       = _scheduleName.Text.Trim();
        _cfg.SheetNumberTemplate        = _sheetNumber.Text.Trim();
        _cfg.SheetNameTemplate          = _sheetName.Text.Trim();

        string? planScale = _planScale.SelectedItem as string;
        _cfg.AutoScale = planScale == AutoFit;
        if (!_cfg.AutoScale) _cfg.PlanScale = ScaleValue(planScale, _cfg.PlanScale);

        _cfg.View3DScale        = ScaleValue(_view3DScale.SelectedItem as string, _cfg.View3DScale);
        _cfg.BendingDetailScale = ScaleValue(_bendingScale.SelectedItem as string, _cfg.BendingDetailScale);

        if (Enum.TryParse(_detailLevel.SelectedItem as string, out RvtDetailLevel detail))
            _cfg.DetailLevel = detail;
        _cfg.VisualStyle = VisualStyleFor(_visualStyle.SelectedItem as string);

        if (Enum.TryParse(_isolation.SelectedItem as string, out LayerIsolation iso))
            _cfg.Isolation = iso;

        string? titleBlock = _titleBlock.SelectedItem as string;
        _cfg.TitleBlockName = string.IsNullOrEmpty(titleBlock) || titleBlock == FirstAvailable ? null : titleBlock;

        string? template = _viewTemplate.SelectedItem as string;
        _cfg.ViewTemplateName = string.IsNullOrEmpty(template) || template == NoTemplate ? null : template;

        _cfg.ShowMiddleBarOnly    = _middleBar.IsChecked == true;
        _cfg.CreateSections       = _sections.IsChecked == true;
        _cfg.Create3DView         = _create3D.IsChecked == true;
        _cfg.CreateBendingDetails = _bending.IsChecked == true;
        _cfg.CreateSchedule       = _schedule.IsChecked == true;
        _cfg.PlaceOnSheet         = _sheet.IsChecked == true;

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
        return Scales.First(s => s.Value == 96).Label;     // fall back to 1/8"=1'-0"
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
            if (string.Equals(v.Label, label, StringComparison.Ordinal)) return v.Style;
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
        foreach (string item in items) combo.Items.Add(item);
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

    private StackPanel BuildSelectionRow(int slabCount)
    {
        var label = new TextBlock
        {
            Text = slabCount == 1 ? "1 slab selected" : $"{slabCount} slabs selected",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var select = new Button { Content = "Select slabs…", Width = 130 };
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
            IsEnabled = _slabCount > 0,
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

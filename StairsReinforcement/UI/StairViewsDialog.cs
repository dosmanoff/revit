using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using StairsReinforcement.Config;

namespace StairsReinforcement.UI;

/// <summary>Code-only dialog for Stair Views: section type + scales + crop, plan, rebar-display and
/// annotation toggles. The section-type list is the document's Section ViewFamilyTypes.</summary>
public sealed class StairViewsDialog : Window
{
    private readonly StairViewsConfig _cfg;

    private readonly ComboBox _sectionType;
    private readonly TextBox _scale;
    private readonly TextBox _planScale;
    private readonly TextBox _crop;
    private readonly CheckBox _plan;
    private readonly CheckBox _secondSection;
    private readonly CheckBox _hideForeign;
    private readonly CheckBox _firstLast;
    private readonly CheckBox _unobscured;
    private readonly CheckBox _tags;
    private readonly CheckBox _spacing;
    private readonly CheckBox _schedule;
    private readonly CheckBox _sheet;

    public StairViewsDialog(StairViewsConfig cfg, IReadOnlyList<string> sectionTypeNames)
    {
        _cfg = cfg;
        Title = "Stair Views";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        _sectionType = new ComboBox { IsEditable = true };
        foreach (string n in sectionTypeNames) _sectionType.Items.Add(n);
        _sectionType.Text = cfg.SectionViewTypeName ?? "Building Section";

        _scale = new TextBox { Text = cfg.ViewScale.ToString() };
        _planScale = new TextBox { Text = cfg.PlanScale.ToString() };
        _crop = new TextBox { Text = cfg.CropPadding.ToString("0.##") };

        _plan = Check("Create plan view", cfg.CreatePlan);
        _secondSection = Check("Second section if flights aren't parallel", cfg.SecondSectionWhenNotParallel);
        _hideForeign = Check("Hide other stairs' rebar", cfg.HideForeignRebar);
        _firstLast = Check("Show sets First/Last", cfg.RebarFirstLast);
        _unobscured = Check("Own bars unobscured", cfg.OwnRebarUnobscured);
        _tags = Check("Rebar tags", cfg.CreateTags);
        _spacing = Check("Spacing dimensions (MRA)", cfg.CreateSpacingAnnotations);
        _schedule = Check("Create rebar schedule", cfg.CreateSchedule);
        _sheet = Check("Place views on a sheet", cfg.PlaceOnSheet);

        var ok = new Button { Content = "Create views", Width = 110, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { WriteBack(); DialogResult = true; };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(Row("Section type:", _sectionType));
        root.Children.Add(Row("Section scale (1:n):", _scale));
        root.Children.Add(Row("Plan scale (1:n):", _planScale));
        root.Children.Add(Row("Crop padding (ft):", _crop));
        root.Children.Add(Sep());
        foreach (CheckBox c in new[] { _plan, _secondSection, _hideForeign, _firstLast, _unobscured, _tags, _spacing, _schedule, _sheet })
            root.Children.Add(c);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
    }

    private void WriteBack()
    {
        string st = _sectionType.Text.Trim();
        _cfg.SectionViewTypeName = string.IsNullOrEmpty(st) ? null : st;
        if (int.TryParse(_scale.Text.Trim(), out int s) && s > 0) _cfg.ViewScale = s;
        if (int.TryParse(_planScale.Text.Trim(), out int ps) && ps > 0) _cfg.PlanScale = ps;
        if (double.TryParse(_crop.Text.Trim(), out double cp) && cp >= 0) _cfg.CropPadding = cp;

        _cfg.CreatePlan = _plan.IsChecked == true;
        _cfg.SecondSectionWhenNotParallel = _secondSection.IsChecked == true;
        _cfg.HideForeignRebar = _hideForeign.IsChecked == true;
        _cfg.RebarFirstLast = _firstLast.IsChecked == true;
        _cfg.OwnRebarUnobscured = _unobscured.IsChecked == true;
        _cfg.CreateTags = _tags.IsChecked == true;
        _cfg.CreateSpacingAnnotations = _spacing.IsChecked == true;
        _cfg.CreateSchedule = _schedule.IsChecked == true;
        _cfg.PlaceOnSheet = _sheet.IsChecked == true;
    }

    private static CheckBox Check(string label, bool on) =>
        new() { Content = label, IsChecked = on, Margin = new Thickness(0, 3, 0, 3) };

    private static FrameworkElement Sep() =>
        new Separator { Margin = new Thickness(0, 8, 0, 6) };

    private static FrameworkElement Row(string label, FrameworkElement field)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        p.Children.Add(new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center });
        field.Width = 240;
        p.Children.Add(field);
        return p;
    }
}

using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using SlabReinforcement.Config;
using Grid = System.Windows.Controls.Grid;

namespace SlabReinforcement.UI;

/// <summary>
/// Code-only WPF dialog for Generate Slab Rebar: choose a single JSON config for all slabs
/// or a per-slab assignments CSV, optionally override the max bar length, and toggle dry run.
/// Persistence of the chosen paths happens in the command (needs a transaction).
/// </summary>
public sealed class SlabRebarGenDialog : Window
{
    private readonly RadioButton _rbCsv;
    private readonly RadioButton _rbBrief;
    private readonly TextBox _folder;
    private readonly ComboBox _configFile;
    private readonly TextBox _csv;
    private readonly TextBox _zones;
    private readonly TextBox _brief;
    private readonly TextBox _maxBar;
    private readonly CheckBox _dryRun;

    public bool FromCsv => _rbCsv.IsChecked == true;
    public bool FromBrief => _rbBrief.IsChecked == true;
    public string? ConfigFolder => Clean(_folder.Text);
    public string? ConfigPath => _configFile.SelectedItem as string;
    public string? CsvPath => Clean(_csv.Text);
    public string? ZonesPath => Clean(_zones.Text);
    public string? BriefPath => Clean(_brief.Text);
    public string? MaxBarOverride => Clean(_maxBar.Text);
    public bool DryRun => _dryRun.IsChecked == true;

    public SlabRebarGenDialog(Document doc)
    {
        Title = "Generate Slab Rebar";
        Width = 580;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var rbSame = new RadioButton { Content = "Same for all — single JSON config", IsChecked = true, FontWeight = FontWeights.Bold };
        _rbCsv = new RadioButton { Content = "From CSV — per-slab by Mark", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) };
        _rbBrief = new RadioButton { Content = "From JSON brief — full per-slab spec (edges, groups)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) };

        _folder = new TextBox { Text = FolderStorage.GetConfigFolder(doc) ?? "" };
        _configFile = new ComboBox();
        _csv = new TextBox { Text = FolderStorage.GetCsvPath(doc) ?? "" };
        _zones = new TextBox { Text = FolderStorage.GetZonesPath(doc) ?? "" };
        _brief = new TextBox();
        _maxBar = new TextBox();
        _dryRun = new CheckBox { Content = "Dry run (place, then roll back — preview only)", Margin = new Thickness(0, 8, 0, 8) };

        var browseFolder = MakeButton("Browse…", () =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog();
            if (d.ShowDialog() == true) { _folder.Text = d.FolderName; ReloadConfigs(); }
        });
        var browseCsv = MakeButton("Browse…", () => PickFile(_csv));
        var browseZones = MakeButton("Browse…", () => PickFile(_zones));
        var browseBrief = MakeButton("Browse…", () => PickFile(_brief));

        var ok = new Button { Content = "Run", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };

        ReloadConfigs();

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(rbSame);
        root.Children.Add(LabeledRow("Config folder:", _folder, browseFolder));
        root.Children.Add(LabeledRow("Config file:", _configFile, null));
        root.Children.Add(_rbCsv);
        root.Children.Add(LabeledRow("Assignments CSV:", _csv, browseCsv));
        root.Children.Add(LabeledRow("Zones CSV (optional):", _zones, browseZones));
        root.Children.Add(_rbBrief);
        root.Children.Add(LabeledRow("JSON brief:", _brief, browseBrief));
        root.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 8) });
        root.Children.Add(LabeledRow("Max bar length override:", _maxBar, null));
        root.Children.Add(_dryRun);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
    }

    private void ReloadConfigs()
    {
        _configFile.Items.Clear();
        foreach (string p in ConfigLoader.EnumerateConfigFiles(_folder.Text)) _configFile.Items.Add(p);
        if (_configFile.Items.Count > 0) _configFile.SelectedIndex = 0;
    }

    private static void PickFile(TextBox target)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*" };
        if (d.ShowDialog() == true) target.Text = d.FileName;
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Content = text, Width = 80 };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static FrameworkElement LabeledRow(string label, FrameworkElement field, Button? button)
    {
        var grid = new Grid { Margin = new Thickness(18, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        field.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);

        if (button is not null)
        {
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }
        return grid;
    }

    private static string? Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

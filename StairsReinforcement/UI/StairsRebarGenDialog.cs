using System.IO;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using StairsReinforcement.Config;

namespace StairsReinforcement.UI;

/// <summary>
/// Code-only WPF dialog for Generate Stair Rebar: choose one JSON config for all stairs, or a
/// per-Mark assignments CSV; toggle a dry run. Paths are seeded from / saved to <see cref="FolderStorage"/>.
/// </summary>
public sealed class StairsRebarGenDialog : Window
{
    private readonly RadioButton _modeJson = new() { Content = "Same config for all (JSON)", IsChecked = true };
    private readonly RadioButton _modeCsv = new() { Content = "From assignments CSV (per Mark)" };
    private readonly TextBox _folder = new();
    private readonly ComboBox _configs = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _csv = new();
    private readonly CheckBox _dryRun = new() { Content = "Dry run (roll back — place nothing)", Margin = new Thickness(0, 8, 0, 0) };

    public bool FromCsv => _modeCsv.IsChecked == true;
    public string? ConfigFolder => Nz(_folder.Text);
    public string? ConfigPath => _configs.SelectedItem as string;
    public string? CsvPath => Nz(_csv.Text);
    public bool DryRun => _dryRun.IsChecked == true;

    public StairsRebarGenDialog(Document doc)
    {
        Title = "Generate Stair Rebar";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        _folder.Text = FolderStorage.GetConfigFolder(doc) ?? "";
        _csv.Text = FolderStorage.GetCsvPath(doc) ?? "";
        RefreshConfigs();

        var root = new StackPanel { Margin = new Thickness(14) };

        root.Children.Add(_modeJson);
        root.Children.Add(Row("Config folder:", _folder, "Browse…", BrowseFolder));
        root.Children.Add(_configs);
        root.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });
        root.Children.Add(_modeCsv);
        root.Children.Add(Row("CSV file:", _csv, "Browse…", BrowseCsv));
        root.Children.Add(_dryRun);

        var run = new Button { Content = "Run", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        run.Click += (_, _) => { DialogResult = true; Close(); };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(run);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
    }

    private void RefreshConfigs()
    {
        _configs.Items.Clear();
        foreach (string p in ConfigLoader.EnumerateConfigFiles(_folder.Text))
            _configs.Items.Add(p);
        if (_configs.Items.Count > 0) _configs.SelectedIndex = 0;
    }

    private StackPanel Row(string label, TextBox box, string btnText, Action onClick)
    {
        box.Width = 360;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(box);
        var btn = new Button { Content = btnText, Width = 70, Margin = new Thickness(6, 0, 0, 0) };
        btn.Click += (_, _) => onClick();
        panel.Children.Add(btn);
        return panel;
    }

    private void BrowseFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Config folder" };
        if (Directory.Exists(_folder.Text)) dlg.InitialDirectory = _folder.Text;
        if (dlg.ShowDialog() == true) { _folder.Text = dlg.FolderName; RefreshConfigs(); _modeJson.IsChecked = true; }
    }

    private void BrowseCsv()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Assignments CSV", Filter = "CSV (*.csv)|*.csv" };
        if (dlg.ShowDialog() == true) { _csv.Text = dlg.FileName; _modeCsv.IsChecked = true; }
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

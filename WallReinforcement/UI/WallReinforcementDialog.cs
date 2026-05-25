using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WallReinforcement.Config;
using WpfGrid = System.Windows.Controls.Grid;

namespace WallReinforcement.UI;

/// <summary>
/// Code-only WPF dialog: pick a config folder, pick a config file, choose dry-run vs apply.
/// No XAML so the project compiles cleanly on Linux CI (same constraint as AutoNumbering).
/// </summary>
public class WallReinforcementDialog : Window
{
    private TextBox _txtFolder = null!;
    private ComboBox _cmbConfig = null!;
    private CheckBox _chkDryRun = null!;
    private TextBlock _txtSummary = null!;

    public string? FolderPath  { get; private set; }
    public string? ConfigPath  { get; private set; }
    public ReinforcementConfig? Config { get; private set; }
    public bool DryRun => _chkDryRun.IsChecked == true;

    public WallReinforcementDialog(string? initialFolder, int selectedWallCount)
    {
        Title  = "Wall Reinforcement";
        Width  = 560;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUI(selectedWallCount);

        FolderPath = initialFolder;
        if (!string.IsNullOrEmpty(initialFolder))
        {
            _txtFolder.Text = initialFolder;
            RefreshConfigs();
        }
    }

    private void BuildUI(int wallCount)
    {
        var grid = new WpfGrid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var selRow = new TextBlock
        {
            Text = $"{wallCount} wall(s) selected",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        WpfGrid.SetRow(selRow, 0);
        grid.Children.Add(selRow);

        grid.Children.Add(BuildFolderRow());
        grid.Children.Add(BuildConfigRow());
        grid.Children.Add(BuildDryRunRow());

        _txtSummary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 8, 0, 0),
        };
        WpfGrid.SetRow(_txtSummary, 4);
        grid.Children.Add(_txtSummary);

        grid.Children.Add(BuildButtonRow());

        Content = grid;
    }

    private UIElement BuildFolderRow()
    {
        _txtFolder = new TextBox { Padding = new Thickness(4, 2, 4, 2) };
        var browse = new Button { Content = "Browse…", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += Browse_Click;

        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(browse, Dock.Right);
        dock.Children.Add(browse);
        dock.Children.Add(_txtFolder);

        var group = new GroupBox { Header = "Config folder", Content = dock, Padding = new Thickness(8, 4, 8, 6) };
        WpfGrid.SetRow(group, 1);
        return group;
    }

    private UIElement BuildConfigRow()
    {
        _cmbConfig = new ComboBox { Padding = new Thickness(4, 2, 4, 2) };
        _cmbConfig.SelectionChanged += Config_SelectionChanged;

        var group = new GroupBox
        {
            Header = "Configuration", Content = _cmbConfig,
            Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 4, 8, 6),
        };
        WpfGrid.SetRow(group, 2);
        return group;
    }

    private UIElement BuildDryRunRow()
    {
        _chkDryRun = new CheckBox
        {
            Content = "Dry run (preview only — no rebar will be committed)",
            Margin = new Thickness(0, 4, 0, 0),
        };
        WpfGrid.SetRow(_chkDryRun, 3);
        return _chkDryRun;
    }

    private UIElement BuildButtonRow()
    {
        var run = new Button { Content = "Run", IsDefault = true, MinWidth = 80, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 0, 0) };
        run.Click += Run_Click;

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 0, 0) };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(run);
        panel.Children.Add(cancel);
        WpfGrid.SetRow(panel, 5);
        return panel;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        // OpenFolderDialog is available in .NET 8 / WPF; falls back to a file dialog with a fake filename otherwise.
        var dlg = new OpenFolderDialog
        {
            Title = "Choose config folder",
            InitialDirectory = Directory.Exists(_txtFolder.Text) ? _txtFolder.Text : "",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _txtFolder.Text = dlg.FolderName;
            FolderPath = dlg.FolderName;
            RefreshConfigs();
        }
    }

    private void RefreshConfigs()
    {
        _cmbConfig.Items.Clear();
        var files = ConfigLoader.EnumerateConfigFiles(_txtFolder.Text);
        foreach (var f in files) _cmbConfig.Items.Add(f);

        if (_cmbConfig.Items.Count == 0)
        {
            _txtSummary.Text = "No *.json configs found in folder.";
            return;
        }

        _cmbConfig.SelectedIndex = 0;
    }

    private void Config_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cmbConfig.SelectedItem is not string path) return;
        try
        {
            Config = ConfigLoader.Load(path);
            ConfigPath = path;
            _txtSummary.Text =
                $"Loaded '{Config.Name}'. " +
                $"Cover ext/int: {Config.Cover.ExteriorMm}/{Config.Cover.InteriorMm} mm. " +
                $"Vertical bars: {Config.FaceMesh.Exterior?.Vertical.BarType} @ {Config.FaceMesh.Exterior?.Vertical.SpacingMm} mm.";
        }
        catch (Exception ex)
        {
            Config = null;
            _txtSummary.Text = $"Failed to load config: {ex.Message}";
        }
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (Config is null)
        {
            MessageBox.Show("Pick a configuration first.", "Wall Reinforcement",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FolderPath = _txtFolder.Text;
        DialogResult = true;
    }
}

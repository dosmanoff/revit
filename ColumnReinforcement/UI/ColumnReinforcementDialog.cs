using ColumnReinforcement.Config;
using ColumnReinforcement.Domain;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WpfGrid = System.Windows.Controls.Grid;

namespace ColumnReinforcement.UI;

/// <summary>
/// Selected mode of the dialog — determines which mapping the command builds.
/// </summary>
public enum RunMode
{
    /// <summary>One JSON config applied to every selected column.</summary>
    Same,

    /// <summary>Per-column configs read from a CSV table, joined on the Mark parameter.</summary>
    FromCsv,
}

/// <summary>
/// Code-only WPF editor for column reinforcement configs. Two modes via the
/// top-level mode tabs: "Same for all" (a JSON config edited in place and
/// applied to every column in the selection) and "From CSV" (per-Mark configs
/// pulled from an external CSV table, with a validation view that flags size
/// and Mark mismatches).
/// </summary>
public class ColumnReinforcementDialog : Window
{
    private TextBox  _txtFolder  = null!;
    private ComboBox _cmbConfig  = null!;
    private ComboBox _cmbUnits   = null!;
    private CheckBox _chkDryRun  = null!;
    private TextBlock _txtStatus = null!;
    private TabControl _tabs     = null!;
    private TabControl _modeTabs = null!;

    // CSV-mode controls.
    private TextBox  _txtCsvPath   = null!;
    private ListView _lvAssignments = null!;
    private CheckBox _chkFallback  = null!;
    private TextBlock _txtCsvIssues = null!;

    private readonly List<Action>       _refreshers = [];
    private readonly List<Func<bool>>   _collectors = [];

    private readonly IList<ColumnInfo> _selectedColumns;

    public string? FolderPath { get; private set; }
    public string? ConfigPath { get; private set; }
    public ColumnReinforcementConfig? Config { get; private set; }
    public bool DryRun => _chkDryRun.IsChecked == true;

    public string? CsvPath { get; private set; }
    public AssignmentTable? Assignments { get; private set; }
    public bool FallbackToJsonForUnassigned => _chkFallback.IsChecked == true;
    public RunMode SelectedMode =>
        _modeTabs.SelectedIndex == 1 ? RunMode.FromCsv : RunMode.Same;

    public ColumnReinforcementDialog(
        string? initialFolder,
        string? initialCsvPath,
        IList<ColumnInfo> selectedColumns)
    {
        _selectedColumns = selectedColumns;

        Title = "Column Reinforcement";
        Width = 760;
        Height = 720;
        MinWidth = 620;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUI(selectedColumns.Count);

        FolderPath = initialFolder;
        if (!string.IsNullOrEmpty(initialFolder))
        {
            _txtFolder.Text = initialFolder;
            RefreshConfigList();
        }

        CsvPath = initialCsvPath;
        if (!string.IsNullOrEmpty(initialCsvPath) && File.Exists(initialCsvPath))
        {
            _txtCsvPath.Text = initialCsvPath;
            TryLoadCsv(initialCsvPath);
        }
    }

    // ── Top-level layout ────────────────────────────────────────────────────

    private void BuildUI(int columnCount)
    {
        var root = new WpfGrid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });     // mode tabs
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // status
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // buttons

        AddRow(root, 0, BuildHeaderRow(columnCount));
        AddRow(root, 1, BuildModeTabs());

        _txtStatus = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 4),
            Foreground = System.Windows.Media.Brushes.Gray,
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
        };
        AddRow(root, 2, _txtStatus);

        AddRow(root, 3, BuildButtonRow());

        Content = root;
    }

    private UIElement BuildModeTabs()
    {
        _modeTabs = new TabControl { Margin = new Thickness(0, 4, 0, 4) };

        var sameForAll = new TabItem
        {
            Header  = "Same for all",
            Content = BuildSameForAllPanel(),
        };
        var fromCsv = new TabItem
        {
            Header  = "From CSV",
            Content = BuildFromCsvPanel(),
        };

        _modeTabs.Items.Add(sameForAll);
        _modeTabs.Items.Add(fromCsv);
        return _modeTabs;
    }

    private UIElement BuildSameForAllPanel()
    {
        var grid = new WpfGrid { Margin = new Thickness(6) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        AddRow(grid, 0, BuildFolderRow());
        AddRow(grid, 1, BuildConfigPickerRow());
        AddRow(grid, 2, BuildEditorTabs());
        return grid;
    }

    private UIElement BuildFromCsvPanel()
    {
        var grid = new WpfGrid { Margin = new Thickness(6) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, BuildCsvPickerRow());
        AddRow(grid, 1, BuildAssignmentsTable());

        _txtCsvIssues = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 4),
            Foreground = System.Windows.Media.Brushes.Firebrick,
            FontStyle  = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
        };
        AddRow(grid, 2, _txtCsvIssues);

        _chkFallback = new CheckBox
        {
            Content = "Fall back to selected JSON config for columns without a CSV assignment",
            Margin  = new Thickness(0, 4, 0, 4),
        };
        AddRow(grid, 3, _chkFallback);

        return grid;
    }

    private UIElement BuildCsvPickerRow()
    {
        _txtCsvPath = new TextBox { Padding = new Thickness(4, 2, 4, 2), IsReadOnly = true };
        var browse  = new Button { Content = "Browse…", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(6, 0, 0, 0) };
        var reload  = new Button { Content = "Reload",  Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 0, 0) };
        browse.Click += BrowseCsv_Click;
        reload.Click += (_, _) => { if (!string.IsNullOrEmpty(_txtCsvPath.Text)) TryLoadCsv(_txtCsvPath.Text); };

        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal };
        rightBtns.Children.Add(browse);
        rightBtns.Children.Add(reload);
        DockPanel.SetDock(rightBtns, Dock.Right);

        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        dock.Children.Add(rightBtns);
        dock.Children.Add(_txtCsvPath);

        return new GroupBox
        {
            Header  = "Assignments CSV",
            Content = dock,
            Padding = new Thickness(8, 4, 8, 6),
            Margin  = new Thickness(0, 0, 0, 4),
        };
    }

    private UIElement BuildAssignmentsTable()
    {
        _lvAssignments = new ListView
        {
            Margin = new Thickness(0, 0, 0, 4),
        };

        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "Mark",       Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.Mark)) });
        gv.Columns.Add(new GridViewColumn { Header = "In CSV",     Width = 60,  DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.InCsv)) });
        gv.Columns.Add(new GridViewColumn { Header = "In Revit",   Width = 70,  DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.InRevit)) });
        gv.Columns.Add(new GridViewColumn { Header = "CSV size",   Width = 90,  DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.CsvSize)) });
        gv.Columns.Add(new GridViewColumn { Header = "Revit size", Width = 100, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.RevitSize)) });
        gv.Columns.Add(new GridViewColumn { Header = "Status",     Width = 200, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.Status)) });
        _lvAssignments.View = gv;

        return new GroupBox
        {
            Header  = "Match: Mark in CSV ↔ Mark in selected columns",
            Content = _lvAssignments,
            Padding = new Thickness(8, 4, 8, 6),
            Margin  = new Thickness(0, 0, 0, 4),
        };
    }

    private static void AddRow(WpfGrid grid, int row, UIElement child)
    {
        WpfGrid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private UIElement BuildHeaderRow(int columnCount)
    {
        var info = new TextBlock
        {
            Text = $"{columnCount} column(s) selected",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _cmbUnits = new ComboBox { Width = 160, VerticalAlignment = VerticalAlignment.Center };
        _cmbUnits.Items.Add(new ComboBoxItem { Content = "Imperial (in)", Tag = UnitSystem.Imperial });
        _cmbUnits.Items.Add(new ComboBoxItem { Content = "Metric (mm)",   Tag = UnitSystem.Metric });
        _cmbUnits.SelectedIndex = 0;
        _cmbUnits.SelectionChanged += (_, _) => OnUnitsChanged();

        var unitsLabel = new Label
        {
            Content = "Units:",
            Padding = new Thickness(0),
            Margin  = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var unitsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        unitsStack.Children.Add(unitsLabel);
        unitsStack.Children.Add(_cmbUnits);
        DockPanel.SetDock(unitsStack, Dock.Right);
        dock.Children.Add(unitsStack);
        dock.Children.Add(info);
        return dock;
    }

    private UIElement BuildFolderRow()
    {
        _txtFolder = new TextBox { Padding = new Thickness(4, 2, 4, 2) };
        var browse = new Button { Content = "Browse…", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += Browse_Click;

        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(browse, Dock.Right);
        dock.Children.Add(browse);
        dock.Children.Add(_txtFolder);

        return new GroupBox
        {
            Header = "Config folder",
            Content = dock,
            Padding = new Thickness(8, 4, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    private UIElement BuildConfigPickerRow()
    {
        _cmbConfig = new ComboBox { Padding = new Thickness(4, 2, 4, 2) };
        _cmbConfig.SelectionChanged += Config_SelectionChanged;

        var btnNew    = MkBtn("New…",     OnNewFromTemplate);
        var btnEdit   = MkBtn("Edit raw", OnEditRaw);
        var btnSave   = MkBtn("Save",     OnSave);
        var btnSaveAs = MkBtn("Save As…", OnSaveAs);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
        btnPanel.Children.Add(btnNew);
        btnPanel.Children.Add(btnEdit);
        btnPanel.Children.Add(btnSave);
        btnPanel.Children.Add(btnSaveAs);
        DockPanel.SetDock(btnPanel, Dock.Right);

        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        dock.Children.Add(btnPanel);
        dock.Children.Add(_cmbConfig);

        return new GroupBox
        {
            Header = "Configuration",
            Content = dock,
            Padding = new Thickness(8, 4, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    private UIElement BuildEditorTabs()
    {
        _tabs = new TabControl { Margin = new Thickness(0, 4, 0, 4) };
        _tabs.Items.Add(MakeSectionTab("Cover",        BuildCoverSection));
        _tabs.Items.Add(MakeSectionTab("Longitudinal", BuildLongitudinalSection));
        _tabs.Items.Add(MakeSectionTab("Ties",         BuildStirrupsSection));
        return _tabs;
    }

    private TabItem MakeSectionTab(string header, Func<UIElement> build) => new()
    {
        Header  = header,
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8),
            Content = build(),
        },
    };

    private UIElement BuildButtonRow()
    {
        _chkDryRun = new CheckBox
        {
            Content = "Dry run (preview only — no rebar will be committed)",
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(_chkDryRun, Dock.Left);

        var run = new Button { Content = "Run", IsDefault = true, MinWidth = 80, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 0, 0) };
        run.Click += Run_Click;
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 0, 0) };

        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        btns.Children.Add(run);
        btns.Children.Add(cancel);
        DockPanel.SetDock(btns, Dock.Right);

        var dock = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        dock.Children.Add(btns);
        dock.Children.Add(_chkDryRun);
        return dock;
    }

    private static Button MkBtn(string text, Action onClick)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(4, 0, 0, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    // ── Section builders ────────────────────────────────────────────────────

    private UIElement BuildCoverSection()
    {
        var (left, _, root) = NewTwoColumnPanel();
        AddLengthRow(left, "Sides", () => Config!.Cover.Sides, v => Config!.Cover.Sides = v);
        AddLengthRow(left, "Ends",  () => Config!.Cover.Ends,  v => Config!.Cover.Ends  = v);
        return root;
    }

    private UIElement BuildLongitudinalSection()
    {
        var (left, right, root) = NewTwoColumnPanel();
        AddTextRow (left,  "Bar type",         () => Config!.Longitudinal.BarType,       s => Config!.Longitudinal.BarType = s);
        AddCheckRow(left,  "Corners only",     () => Config!.Longitudinal.CornerOnly,    b => Config!.Longitudinal.CornerOnly = b);
        AddIntRow  (left,  "Bars along width", 2, 20, () => Config!.Longitudinal.BarsAlongWidth, v => Config!.Longitudinal.BarsAlongWidth = v);
        AddIntRow  (left,  "Bars along depth", 2, 20, () => Config!.Longitudinal.BarsAlongDepth, v => Config!.Longitudinal.BarsAlongDepth = v);
        AddTextRow (right, "Hook type top",    () => Config!.Longitudinal.HookTopType ?? "",     s => Config!.Longitudinal.HookTopType    = string.IsNullOrWhiteSpace(s) ? null : s);
        AddTextRow (right, "Hook type bottom", () => Config!.Longitudinal.HookBottomType ?? "",  s => Config!.Longitudinal.HookBottomType = string.IsNullOrWhiteSpace(s) ? null : s);
        AddTextRow (right, "Cranked shape",    () => Config!.Longitudinal.CrankedShape ?? "",    s => Config!.Longitudinal.CrankedShape    = string.IsNullOrWhiteSpace(s) ? null : s);
        AddTextRow (right, "BentToSlab shape", () => Config!.Longitudinal.TopBentShape ?? "",    s => Config!.Longitudinal.TopBentShape    = string.IsNullOrWhiteSpace(s) ? null : s);
        return root;
    }

    private UIElement BuildStirrupsSection()
    {
        var (left, right, root) = NewTwoColumnPanel();
        AddCheckRow (left,  "Enabled",   () => Config!.Stirrups.Enabled,           b => Config!.Stirrups.Enabled = b);
        AddTextRow  (left,  "Bar type",  () => Config!.Stirrups.BarType,           s => Config!.Stirrups.BarType = s);
        AddLengthRow(left,  "Spacing",   () => Config!.Stirrups.Spacing,           v => Config!.Stirrups.Spacing = v);
        AddTextRow  (right, "Hook type", () => Config!.Stirrups.HookType ?? "",    s => Config!.Stirrups.HookType = string.IsNullOrWhiteSpace(s) ? null : s);
        AddCheckRow (right, "Rotate 45° (Phase 2)", () => Config!.Stirrups.Rotate45, b => Config!.Stirrups.Rotate45 = b);
        AddTextRow  (right, "Tie shape", () => Config!.Stirrups.Shape ?? "",       s => Config!.Stirrups.Shape    = string.IsNullOrWhiteSpace(s) ? null : s);
        return root;
    }

    // ── Field-row helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Two side-by-side StackPanels in a 3-column Grid (star / gutter / star). Each
    /// caller assigns fields to <paramref name="left"/> or <paramref name="right"/>.
    /// Lets large config sections fit screen height without scrolling.
    /// </summary>
    private static (StackPanel left, StackPanel right, UIElement root) NewTwoColumnPanel()
    {
        var grid = new WpfGrid { Margin = new Thickness(2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left  = new StackPanel { Orientation = Orientation.Vertical };
        var right = new StackPanel { Orientation = Orientation.Vertical };
        WpfGrid.SetColumn(left,  0);
        WpfGrid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return (left, right, grid);
    }

    private void AddLengthRow(StackPanel parent, string label, Func<Length> get, Action<Length> set)
    {
        var tb = MakeFieldTextBox();
        parent.Children.Add(MakeFieldRow(label, tb, hint: "in or mm (per Units), or feet-inches like 1'-3\""));

        _refreshers.Add(() =>
        {
            Length v = get();
            tb.Text = v.Text ?? (v.Number?.ToString("G", System.Globalization.CultureInfo.InvariantCulture) ?? "");
        });
        _collectors.Add(() =>
        {
            string raw = (tb.Text ?? "").Trim();
            if (raw.Length == 0) { set(new Length(0)); return true; }
            if (raw.Contains('\'') || raw.Contains('"') || raw.Contains('′') || raw.Contains('″'))
            {
                try { _ = Length.ParseFeetInches(raw); set(new Length(raw)); return true; }
                catch (Exception ex) { _txtStatus.Text = $"{label}: {ex.Message}"; return false; }
            }
            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n))
            { _txtStatus.Text = $"{label}: '{raw}' is not a valid number."; return false; }
            set(new Length(n));
            return true;
        });
    }

    private void AddIntRow(StackPanel parent, string label, int min, int max, Func<int> get, Action<int> set)
    {
        var tb = MakeFieldTextBox();
        parent.Children.Add(MakeFieldRow(label, tb, hint: $"{min}–{max}"));
        _refreshers.Add(() => tb.Text = get().ToString(System.Globalization.CultureInfo.InvariantCulture));
        _collectors.Add(() =>
        {
            if (!int.TryParse((tb.Text ?? "").Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n))
            { _txtStatus.Text = $"{label}: not a valid integer."; return false; }
            if (n < min || n > max)
            { _txtStatus.Text = $"{label}: must be between {min} and {max}."; return false; }
            set(n);
            return true;
        });
    }

    private void AddTextRow(StackPanel parent, string label, Func<string> get, Action<string> set)
    {
        var tb = MakeFieldTextBox();
        parent.Children.Add(MakeFieldRow(label, tb));
        _refreshers.Add(() => tb.Text = get() ?? "");
        _collectors.Add(() => { set((tb.Text ?? "").Trim()); return true; });
    }

    private void AddCheckRow(StackPanel parent, string label, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center };
        parent.Children.Add(MakeFieldRow(label, cb));
        _refreshers.Add(() => cb.IsChecked = get());
        _collectors.Add(() => { set(cb.IsChecked == true); return true; });
    }

    private static TextBox MakeFieldTextBox() => new()
    {
        Padding = new Thickness(4, 2, 4, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static UIElement MakeFieldRow(string label, UIElement field, string? hint = null)
    {
        var grid = new WpfGrid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lbl = new Label { Content = label, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        WpfGrid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        WpfGrid.SetColumn(field, 1);
        grid.Children.Add(field);

        if (hint is not null)
        {
            var h = new TextBlock
            {
                Text = hint, FontSize = 10, Foreground = System.Windows.Media.Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            WpfGrid.SetColumn(h, 2);
            grid.Children.Add(h);
        }

        return grid;
    }

    // ── Event handlers ──────────────────────────────────────────────────────

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose config folder",
            InitialDirectory = Directory.Exists(_txtFolder.Text) ? _txtFolder.Text : "",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _txtFolder.Text = dlg.FolderName;
            FolderPath = dlg.FolderName;
            RefreshConfigList();
        }
    }

    private void RefreshConfigList()
    {
        _cmbConfig.Items.Clear();
        var files = ConfigLoader.EnumerateConfigFiles(_txtFolder.Text);
        foreach (var f in files) _cmbConfig.Items.Add(f);
        if (_cmbConfig.Items.Count > 0)
        {
            _cmbConfig.SelectedIndex = 0;
        }
        else
        {
            _txtStatus.Text = "No *.json configs in this folder. Click 'New…' to bootstrap from a sample.";
        }
    }

    private void Config_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cmbConfig.SelectedItem is not string path) return;
        try
        {
            Config = ConfigLoader.Load(path);
            ConfigPath = path;
            SyncUnitsDropdownFromConfig();
            foreach (var r in _refreshers) r();
            _txtStatus.Text = $"Loaded '{Config.Name}'. Edit any field, then Save or Run.";
        }
        catch (Exception ex)
        {
            Config = null;
            _txtStatus.Text = $"Failed to load: {ex.Message}";
        }
    }

    private void SyncUnitsDropdownFromConfig()
    {
        if (Config is null) return;
        foreach (ComboBoxItem item in _cmbUnits.Items)
        {
            if ((UnitSystem)item.Tag == Config.Units) { _cmbUnits.SelectedItem = item; return; }
        }
    }

    private void OnUnitsChanged()
    {
        if (Config is null) return;
        if (_cmbUnits.SelectedItem is ComboBoxItem item && item.Tag is UnitSystem sys)
            Config.Units = sys;
    }

    private void OnNewFromTemplate()
    {
        if (string.IsNullOrEmpty(_txtFolder.Text) || !Directory.Exists(_txtFolder.Text))
        {
            MessageBox.Show("Pick a config folder first.", "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string samplesDir = BundledSamplesDir();
        if (!Directory.Exists(samplesDir))
        {
            MessageBox.Show($"Bundled samples directory not found:\n{samplesDir}", "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var templates = Directory.EnumerateFiles(samplesDir, "*.json").OrderBy(p => p).ToList();
        if (templates.Count == 0)
        {
            MessageBox.Show("No bundled samples found.", "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var pick = new TemplatePickerDialog(templates) { Owner = this };
        if (pick.ShowDialog() != true || pick.Chosen is null) return;

        string suggested = Path.Combine(_txtFolder.Text, Path.GetFileName(pick.Chosen));
        string target = UniqueTarget(suggested);
        try
        {
            File.Copy(pick.Chosen, target, overwrite: false);
            RefreshConfigList();
            foreach (var it in _cmbConfig.Items)
                if (it is string s && string.Equals(s, target, StringComparison.OrdinalIgnoreCase)) { _cmbConfig.SelectedItem = it; break; }
            _txtStatus.Text = $"Created {Path.GetFileName(target)} from sample. Edit fields and Save.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string UniqueTarget(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext  = Path.GetExtension(path);
        for (int i = 1; i < 1000; i++)
        {
            string candidate = Path.Combine(dir, $"{stem}-{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }

    private void OnEditRaw()
    {
        if (string.IsNullOrEmpty(ConfigPath) || !File.Exists(ConfigPath))
        {
            MessageBox.Show("Pick a configuration first.", "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ConfigPath,
                UseShellExecute = true,
            });
            _txtStatus.Text = "Opened in your default JSON editor. Save there, then re-select the config in this dialog to reload.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSave()
    {
        if (Config is null || string.IsNullOrEmpty(ConfigPath))
        {
            MessageBox.Show("Pick a configuration first.", "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!CollectAll()) return;
        try
        {
            ConfigLoader.Save(Config, ConfigPath);
            _txtStatus.Text = $"Saved to {Path.GetFileName(ConfigPath)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveAs()
    {
        if (Config is null) { MessageBox.Show("Pick or create a configuration first.", "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (!CollectAll()) return;

        var dlg = new SaveFileDialog
        {
            Filter = "JSON config (*.json)|*.json",
            InitialDirectory = _txtFolder.Text,
            FileName = string.IsNullOrEmpty(Config.Name) ? "new-config.json" : Config.Name + ".json",
            DefaultExt = ".json",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Config.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
            ConfigLoader.Save(Config, dlg.FileName);
            ConfigPath = dlg.FileName;
            RefreshConfigList();
            foreach (var it in _cmbConfig.Items)
                if (it is string s && string.Equals(s, dlg.FileName, StringComparison.OrdinalIgnoreCase)) { _cmbConfig.SelectedItem = it; break; }
            _txtStatus.Text = $"Saved as {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CollectAll()
    {
        _txtStatus.Text = "";
        foreach (var c in _collectors)
            if (!c()) return false;
        OnUnitsChanged();
        return true;
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMode == RunMode.Same)
        {
            if (Config is null)
            {
                MessageBox.Show("Pick or create a configuration first, then Run.",
                    "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!CollectAll()) return;
            FolderPath = _txtFolder.Text;
        }
        else // FromCsv
        {
            if (Assignments is null)
            {
                MessageBox.Show("Pick a CSV file first, then Run.",
                    "Column Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Even in CSV mode, the user may still need a fallback JSON config.
            // CollectAll is harmless when called without a loaded config; allow it.
            if (Config is not null && !CollectAll()) return;
            FolderPath = _txtFolder.Text;
        }
        DialogResult = true;
    }

    // ── CSV mode wiring ─────────────────────────────────────────────────────

    private void BrowseCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter      = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title       = "Pick an assignments CSV",
            InitialDirectory = string.IsNullOrEmpty(_txtCsvPath.Text) ? "" : Path.GetDirectoryName(_txtCsvPath.Text),
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        _txtCsvPath.Text = dlg.FileName;
        TryLoadCsv(dlg.FileName);
    }

    private void TryLoadCsv(string path)
    {
        try
        {
            Assignments = AssignmentCsv.Load(path);
            CsvPath     = path;
            RebuildAssignmentsTable();
            _txtCsvIssues.Text = FormatIssues(Assignments.Issues);
            _txtStatus.Text    = $"Loaded {Assignments.ByMark.Count} assignment(s) from {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Assignments = null;
            _lvAssignments.ItemsSource = null;
            _txtCsvIssues.Text = $"Failed to load CSV: {ex.Message}";
            _txtStatus.Text    = "";
        }
    }

    private static string FormatIssues(IReadOnlyList<ParseIssue> issues)
    {
        if (issues.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{issues.Count} parse issue(s):");
        int shown = 0;
        foreach (var i in issues)
        {
            if (shown >= 5) { sb.AppendLine($"  … and {issues.Count - shown} more."); break; }
            string field = string.IsNullOrEmpty(i.Field) ? "" : $" ({i.Field})";
            sb.AppendLine($"  · line {i.LineNumber}{field}: {i.Message}");
            shown++;
        }
        return sb.ToString();
    }

    private void RebuildAssignmentsTable()
    {
        if (Assignments is null)
        {
            _lvAssignments.ItemsSource = null;
            return;
        }

        var rows = new List<AssignmentRow>();

        // Index Revit selection by Mark (case-insensitive). Track duplicates.
        var revitByMark = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        var duplicateMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _selectedColumns)
        {
            if (string.IsNullOrWhiteSpace(c.Mark)) continue;
            if (revitByMark.ContainsKey(c.Mark!)) duplicateMarks.Add(c.Mark!);
            else revitByMark[c.Mark!] = c;
        }

        var csvMarks = new HashSet<string>(Assignments.ByMark.Keys, StringComparer.OrdinalIgnoreCase);

        // Union of Marks, alphabetically.
        var allMarks = revitByMark.Keys.Union(csvMarks, StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase);

        foreach (var mark in allMarks)
        {
            bool inCsv = csvMarks.Contains(mark);
            bool inRev = revitByMark.ContainsKey(mark);

            string csvSize = "";
            string revSize = "";
            var statuses = new List<string>();

            if (inCsv && Assignments.ExpectedByMark.TryGetValue(mark, out var exp) && exp is not null)
                csvSize = FormatExpectedSize(exp);

            if (inRev)
            {
                var info = revitByMark[mark];
                revSize = FormatActualSize(info);
            }

            if (!inRev) statuses.Add("⚠ not in selection");
            if (!inCsv) statuses.Add("⚠ no CSV assignment");
            if (duplicateMarks.Contains(mark)) statuses.Add("⚠ duplicate Mark in selection");

            if (inCsv && inRev && Assignments.ExpectedByMark.TryGetValue(mark, out var exp2) && exp2 is not null)
            {
                var info = revitByMark[mark];
                if (exp2.Section != info.Section)
                    statuses.Add($"⚠ section: CSV={exp2.Section}, Revit={info.Section}");
                else if (IsSizeMismatch(exp2, info))
                    statuses.Add("⚠ size mismatch");
            }

            if (statuses.Count == 0) statuses.Add("OK");

            rows.Add(new AssignmentRow
            {
                Mark      = mark,
                InCsv     = inCsv ? "✓" : "—",
                InRevit   = inRev ? "✓" : "—",
                CsvSize   = csvSize,
                RevitSize = revSize,
                Status    = string.Join(", ", statuses),
            });
        }

        // Selected columns with empty Mark — they can't be matched to any CSV row.
        foreach (var c in _selectedColumns.Where(c => string.IsNullOrWhiteSpace(c.Mark)))
        {
            rows.Add(new AssignmentRow
            {
                Mark      = "(empty)",
                InCsv     = "—",
                InRevit   = "✓",
                CsvSize   = "",
                RevitSize = FormatActualSize(c),
                Status    = "⚠ no Mark on column",
            });
        }

        _lvAssignments.ItemsSource = rows;
    }

    private static bool IsSizeMismatch(ExpectedGeometry exp, ColumnInfo info)
    {
        const double tol = 0.01;  // 0.01 in = 1/100 inch
        if (exp.Section == ColumnSection.Round)
        {
            return exp.DiameterIn is double d && Math.Abs(d - info.WidthIn) > tol;
        }
        bool wOk = exp.WidthIn is null || Math.Abs(exp.WidthIn.Value - info.WidthIn) <= tol;
        bool dOk = exp.DepthIn is null || Math.Abs(exp.DepthIn.Value - info.DepthIn) <= tol;
        return !(wOk && dOk);
    }

    private static string FormatExpectedSize(ExpectedGeometry e)
    {
        if (e.Section == ColumnSection.Round)
            return e.DiameterIn is double v ? $"⌀{v:0.##}\"" : "";
        string w = e.WidthIn is double wv ? $"{wv:0.##}\"" : "?";
        string d = e.DepthIn is double dv ? $"{dv:0.##}\"" : "?";
        return $"{w}×{d}";
    }

    private static string FormatActualSize(ColumnInfo info)
    {
        if (info.Section == ColumnSection.Round)
            return $"⌀{info.WidthIn:0.##}\"";
        return $"{info.WidthIn:0.##}\"×{info.DepthIn:0.##}\"";
    }

    /// <summary>Row in the CSV-mode validation table.</summary>
    public class AssignmentRow
    {
        public string Mark      { get; init; } = "";
        public string InCsv     { get; init; } = "";
        public string InRevit   { get; init; } = "";
        public string CsvSize   { get; init; } = "";
        public string RevitSize { get; init; } = "";
        public string Status    { get; init; } = "";
    }

    private static string BundledSamplesDir() =>
        Path.Combine(Path.GetDirectoryName(typeof(ColumnReinforcementDialog).Assembly.Location)!, "samples");
}

/// <summary>Tiny modal listbox that returns the chosen template path.</summary>
internal class TemplatePickerDialog : Window
{
    private readonly ListBox _list;
    public string? Chosen { get; private set; }

    public TemplatePickerDialog(IEnumerable<string> templates)
    {
        Title = "Pick a sample";
        Width = 480;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list = new ListBox { Margin = new Thickness(8) };
        foreach (var t in templates) _list.Items.Add(t);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        var ok = new Button { Content = "Use sample", IsDefault = true, MinWidth = 100, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 0, 0) };
        ok.Click += (_, _) => { Chosen = _list.SelectedItem as string; DialogResult = Chosen is not null; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 8, 0) };

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);

        var dock = new DockPanel();
        DockPanel.SetDock(btns, Dock.Bottom);
        dock.Children.Add(btns);
        dock.Children.Add(_list);

        Content = dock;
    }
}

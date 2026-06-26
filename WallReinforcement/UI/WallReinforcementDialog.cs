using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WallReinforcement.Config;
using WpfGrid = System.Windows.Controls.Grid;

namespace WallReinforcement.UI;

/// <summary>
/// Code-only WPF editor: pick a config file (or create a new one from template), edit every
/// parameter in place, save back to disk, then Run / Dry-run.
///
/// Layout:
///   ┌─ wall info ─ Units dropdown ──────────────────────┐
///   ├─ Folder ──────────────────────────────── [Browse] │
///   ├─ Config dropdown ─ [New] [Edit] [Save] [Save As]  │
///   ├─ TabControl: Cover / Face mesh / Openings / ...   │
///   ├─ Status text                                       │
///   └─ Dry run                  [Run]  [Cancel]          │
/// </summary>
public class WallReinforcementDialog : Window
{
    private TextBox  _txtFolder    = null!;
    private ComboBox _cmbConfig    = null!;
    private ComboBox _cmbUnits     = null!;
    private CheckBox _chkDryRun    = null!;
    private CheckBox _chkBrief     = null!;
    private TextBox  _txtBrief     = null!;
    private TextBlock _txtStatus   = null!;
    private TabControl _tabs       = null!;

    // Section panels rebuilt on every config-load so they always reflect the latest POCO.
    private readonly List<Action> _refreshers = [];
    private readonly List<Func<bool>> _collectors = [];

    public string? FolderPath { get; private set; }
    public string? ConfigPath { get; private set; }
    public ReinforcementConfig? Config { get; private set; }
    public bool DryRun => _chkDryRun.IsChecked == true;

    /// <summary>When true, ignore the single config below and drive each wall from <see cref="BriefPath"/>.</summary>
    public bool UseBrief => _chkBrief.IsChecked == true;
    public string? BriefPath { get; private set; }

    public WallReinforcementDialog(string? initialFolder, int selectedWallCount)
    {
        Title = "Wall Reinforcement";
        Width = 720;
        Height = 720;
        MinWidth = 600;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUI(selectedWallCount);

        FolderPath = initialFolder;
        if (!string.IsNullOrEmpty(initialFolder))
        {
            _txtFolder.Text = initialFolder;
            RefreshConfigList();
        }
    }

    // ── Top-level layout ────────────────────────────────────────────────────

    private void BuildUI(int wallCount)
    {
        var root = new WpfGrid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 0 header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 1 folder
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 2 config picker
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 3 brief
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4 tabs
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 5 status
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 6 buttons

        AddRow(root, 0, BuildHeaderRow(wallCount));
        AddRow(root, 1, BuildFolderRow());
        AddRow(root, 2, BuildConfigPickerRow());
        AddRow(root, 3, BuildBriefRow());
        AddRow(root, 4, BuildEditorTabs());

        _txtStatus = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 4),
            Foreground = System.Windows.Media.Brushes.Gray,
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
        };
        AddRow(root, 5, _txtStatus);

        AddRow(root, 6, BuildButtonRow());

        Content = root;
    }

    private static void AddRow(WpfGrid grid, int row, UIElement child)
    {
        WpfGrid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private UIElement BuildHeaderRow(int wallCount)
    {
        var wallInfo = new TextBlock
        {
            Text = $"{wallCount} wall(s) selected",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _cmbUnits = new ComboBox { Width = 160, VerticalAlignment = VerticalAlignment.Center };
        _cmbUnits.Items.Add(new ComboBoxItem { Content = "Metric (mm)",   Tag = UnitSystem.Metric });
        _cmbUnits.Items.Add(new ComboBoxItem { Content = "Imperial (in)", Tag = UnitSystem.Imperial });
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
        dock.Children.Add(wallInfo);
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

    private UIElement BuildBriefRow()
    {
        _chkBrief = new CheckBox
        {
            Content = "Use per-wall JSON brief (overrides the config below; matches each wall by Mark / Id)",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _chkBrief.Checked   += (_, _) => OnBriefToggled();
        _chkBrief.Unchecked += (_, _) => OnBriefToggled();

        _txtBrief = new TextBox { Padding = new Thickness(4, 2, 4, 2), IsEnabled = false };
        var browse = new Button { Content = "Browse…", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };
        browse.Click += BrowseBrief_Click;

        var pathDock = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        pathDock.Children.Add(browse);
        pathDock.Children.Add(_txtBrief);

        // Keep the path controls' enabled-state synced with the checkbox.
        _chkBrief.Checked   += (_, _) => { _txtBrief.IsEnabled = true;  browse.IsEnabled = true; };
        _chkBrief.Unchecked += (_, _) => { _txtBrief.IsEnabled = false; browse.IsEnabled = false; };

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(_chkBrief);
        stack.Children.Add(pathDock);

        return new GroupBox
        {
            Header = "Agent brief",
            Content = stack,
            Padding = new Thickness(8, 4, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    private void OnBriefToggled()
    {
        bool on = _chkBrief.IsChecked == true;
        if (_tabs is not null) _tabs.IsEnabled = !on;
        _txtStatus.Text = on
            ? "Brief mode: each wall is matched by Mark / Id and reinforced from the brief. The config tabs are ignored."
            : "";
    }

    private void BrowseBrief_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose wall reinforcement brief (JSON)",
            Filter = "JSON brief (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(_txtFolder.Text) ? _txtFolder.Text : "",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _txtBrief.Text = dlg.FileName;
            BriefPath = dlg.FileName;
        }
    }

    private UIElement BuildEditorTabs()
    {
        _tabs = new TabControl { Margin = new Thickness(0, 4, 0, 4) };
        _tabs.Items.Add(MakeSectionTab("Cover",      BuildCoverSection));
        _tabs.Items.Add(MakeSectionTab("Face Mesh",  BuildFaceMeshSection));
        _tabs.Items.Add(MakeSectionTab("Openings",   BuildOpeningsSection));
        _tabs.Items.Add(MakeSectionTab("Edges",      BuildEdgesSection));
        _tabs.Items.Add(MakeSectionTab("Ties",       BuildTiesSection));
        _tabs.Items.Add(MakeSectionTab("Corners",    BuildCornersSection));
        _tabs.Items.Add(MakeSectionTab("T-Junctions",BuildTJunctionsSection));
        _tabs.Items.Add(MakeSectionTab("Anchorage",  BuildAnchorageSection));
        return _tabs;
    }

    private TabItem MakeSectionTab(string header, Func<UIElement> build)
    {
        return new TabItem
        {
            Header  = header,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                Content = build(),
            },
        };
    }

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
        var panel = NewSectionPanel();
        AddLengthRow(panel, "Exterior",     () => Config!.Cover.Exterior, v => Config!.Cover.Exterior = v);
        AddLengthRow(panel, "Interior",     () => Config!.Cover.Interior, v => Config!.Cover.Interior = v);
        AddLengthRow(panel, "Top",          () => Config!.Cover.Top,      v => Config!.Cover.Top      = v);
        AddLengthRow(panel, "Bottom",       () => Config!.Cover.Bottom,   v => Config!.Cover.Bottom   = v);
        AddLengthRow(panel, "Ends",         () => Config!.Cover.Ends,     v => Config!.Cover.Ends     = v);
        return panel;
    }

    private UIElement BuildFaceMeshSection()
    {
        var panel = NewSectionPanel();
        panel.Children.Add(MakeGroup("Exterior face", BuildFacePanel(() => Config!.FaceMesh.Exterior ??= new FaceConfig(), () => Config!.FaceMesh.Exterior!)));
        panel.Children.Add(MakeGroup("Interior face", BuildFacePanel(() => Config!.FaceMesh.Interior ??= new FaceConfig(), () => Config!.FaceMesh.Interior!)));
        return panel;
    }

    private UIElement BuildFacePanel(Action ensure, Func<FaceConfig> get)
    {
        var p = NewSectionPanel();
        AddNote(p, "Vertical bars");
        AddTextRow  (p, "Bar type", () => { ensure(); return get().Vertical.BarType; }, s => { ensure(); get().Vertical.BarType = s; });
        AddLengthRow(p, "Spacing",  () => { ensure(); return get().Vertical.Spacing; }, v => { ensure(); get().Vertical.Spacing = v; });
        AddNote(p, "Horizontal bars");
        AddTextRow  (p, "Bar type", () => { ensure(); return get().Horizontal.BarType; }, s => { ensure(); get().Horizontal.BarType = s; });
        AddLengthRow(p, "Spacing",  () => { ensure(); return get().Horizontal.Spacing; }, v => { ensure(); get().Horizontal.Spacing = v; });
        return p;
    }

    private UIElement BuildOpeningsSection()
    {
        var panel = NewSectionPanel();
        AddCheckRow (panel, "Enabled",       () => Config!.Openings.Enabled,    b => Config!.Openings.Enabled = b);
        AddTextRow  (panel, "Bar type",      () => Config!.Openings.BarType,    s => Config!.Openings.BarType = s);
        AddLengthRow(panel, "Extension",     () => Config!.Openings.Extension,  v => Config!.Openings.Extension = v);
        AddLengthRow(panel, "Min width",     () => Config!.Openings.MinWidth,   v => Config!.Openings.MinWidth = v);

        panel.Children.Add(MakeGroup("Diagonals", BuildDiagonalsPanel()));
        return panel;
    }

    private UIElement BuildDiagonalsPanel()
    {
        var p = NewSectionPanel();
        AddCheckRow (p, "Enabled",   () => Config!.Openings.Diagonals.Enabled,  b => Config!.Openings.Diagonals.Enabled = b);
        AddTextRow  (p, "Bar type",  () => Config!.Openings.Diagonals.BarType,  s => Config!.Openings.Diagonals.BarType = s);
        AddLengthRow(p, "Length",    () => Config!.Openings.Diagonals.Length,   v => Config!.Openings.Diagonals.Length = v);
        AddDoubleRow(p, "Angle (°)", () => Config!.Openings.Diagonals.AngleDeg, v => Config!.Openings.Diagonals.AngleDeg = v);
        return p;
    }

    private UIElement BuildEdgesSection()
    {
        var panel = NewSectionPanel();
        panel.Children.Add(MakeGroup("Top edge",    BuildEdgePanel(() => Config!.Edges.Top)));
        panel.Children.Add(MakeGroup("Bottom edge", BuildEdgePanel(() => Config!.Edges.Bottom)));
        panel.Children.Add(MakeGroup("Ends",        BuildEdgePanel(() => Config!.Edges.Ends)));
        return panel;
    }

    private UIElement BuildEdgePanel(Func<EdgeConfig> get)
    {
        var p = NewSectionPanel();
        AddCheckRow (p, "Enabled",   () => get().Enabled,   b => get().Enabled = b);
        AddTextRow  (p, "Bar type",  () => get().BarType,   s => get().BarType = s);
        AddLengthRow(p, "Leg length",() => get().LegLength, v => get().LegLength = v);
        AddLengthRow(p, "Spacing",   () => get().Spacing,   v => get().Spacing = v);
        return p;
    }

    private UIElement BuildTiesSection()
    {
        var panel = NewSectionPanel();
        AddCheckRow (panel, "Enabled",        () => Config!.Ties.Enabled,      b => Config!.Ties.Enabled = b);
        AddTextRow  (panel, "Bar type",       () => Config!.Ties.BarType,      s => Config!.Ties.BarType = s);
        AddLengthRow(panel, "Spacing X",      () => Config!.Ties.SpacingX,     v => Config!.Ties.SpacingX = v);
        AddLengthRow(panel, "Spacing Y",      () => Config!.Ties.SpacingY,     v => Config!.Ties.SpacingY = v);
        AddLengthRow(panel, "Min wall thick", () => Config!.Ties.MinThickness, v => Config!.Ties.MinThickness = v);
        return panel;
    }

    private UIElement BuildCornersSection()
    {
        var panel = NewSectionPanel();
        AddCheckRow (panel, "Enabled",    () => Config!.Corners.Enabled,   b => Config!.Corners.Enabled = b);
        AddTextRow  (panel, "Bar type",   () => Config!.Corners.BarType,   s => Config!.Corners.BarType = s);
        AddLengthRow(panel, "Lap length", () => Config!.Corners.LapLength, v => Config!.Corners.LapLength = v);
        AddLengthRow(panel, "Spacing",    () => Config!.Corners.Spacing,   v => Config!.Corners.Spacing = v);
        return panel;
    }

    private UIElement BuildTJunctionsSection()
    {
        var panel = NewSectionPanel();
        AddCheckRow (panel, "Enabled",    () => Config!.TJunctions.Enabled,   b => Config!.TJunctions.Enabled = b);
        AddTextRow  (panel, "Bar type",   () => Config!.TJunctions.BarType,   s => Config!.TJunctions.BarType = s);
        AddLengthRow(panel, "Lap length", () => Config!.TJunctions.LapLength, v => Config!.TJunctions.LapLength = v);
        AddLengthRow(panel, "Spacing",    () => Config!.TJunctions.Spacing,   v => Config!.TJunctions.Spacing = v);
        return panel;
    }

    private UIElement BuildAnchorageSection()
    {
        var panel = NewSectionPanel();
        AddNote(panel, "ACI 318-19 length governor");
        panel.Children.Add(new TextBlock
        {
            Text = "When ON: edge legs & opening-trim extensions use the development length ℓd, and "
                 + "corner / T laps use the Class B tension lap ℓst — sized per bar. Needs Imperial "
                 + "units + ASTM #-bars; otherwise the typed lengths are kept as a fallback.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 6),
        });
        AddCheckRow (panel, "Use ACI 318-19",          () => Config!.Anchorage.Mode == AnchorMode.Aci,
                                                        b  => Config!.Anchorage.Mode = b ? AnchorMode.Aci : AnchorMode.Explicit);
        AddDoubleRow(panel, "f'c (psi)",               () => Config!.Anchorage.FcPsi,           v => Config!.Anchorage.FcPsi = v);
        AddDoubleRow(panel, "fy (psi)",                () => Config!.Anchorage.FyPsi,           v => Config!.Anchorage.FyPsi = v);
        AddCheckRow (panel, "Epoxy-coated",            () => Config!.Anchorage.Epoxy,           b => Config!.Anchorage.Epoxy = b);
        AddCheckRow (panel, "Lightweight concrete",    () => Config!.Anchorage.Lightweight,     b => Config!.Anchorage.Lightweight = b);
        AddCheckRow (panel, "Adequate spacing/cover",  () => Config!.Anchorage.AdequateSpacing, b => Config!.Anchorage.AdequateSpacing = b);
        return panel;
    }

    // ── Field-row helpers ───────────────────────────────────────────────────

    private static StackPanel NewSectionPanel() => new() { Orientation = Orientation.Vertical, Margin = new Thickness(2) };

    private static GroupBox MakeGroup(string header, UIElement body) => new GroupBox
    {
        Header = header,
        Content = body,
        Padding = new Thickness(6, 4, 6, 4),
        Margin = new Thickness(0, 2, 0, 6),
    };

    private static void AddNote(StackPanel parent, string text) =>
        parent.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 2),
        });

    private void AddLengthRow(StackPanel parent, string label, Func<Length> get, Action<Length> set)
    {
        var tb = MakeFieldTextBox();
        parent.Children.Add(MakeFieldRow(label, tb, hint: "mm or in (per Units), or feet-inches like 1'-3\""));

        _refreshers.Add(() =>
        {
            Length v = get();
            tb.Text = v.Text ?? (v.Number?.ToString("G", System.Globalization.CultureInfo.InvariantCulture) ?? "");
        });
        _collectors.Add(() =>
        {
            string raw = (tb.Text ?? "").Trim();
            if (raw.Length == 0) { set(new Length(0)); return true; }
            // A string containing ' or " is treated as feet-inches; otherwise as a plain number.
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

    private void AddDoubleRow(StackPanel parent, string label, Func<double> get, Action<double> set)
    {
        var tb = MakeFieldTextBox();
        parent.Children.Add(MakeFieldRow(label, tb));
        _refreshers.Add(() => tb.Text = get().ToString("G", System.Globalization.CultureInfo.InvariantCulture));
        _collectors.Add(() =>
        {
            if (!double.TryParse((tb.Text ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n))
            { _txtStatus.Text = $"{label}: not a valid number."; return false; }
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
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
            _txtStatus.Text = "No *.json configs in this folder. Click 'New…' to bootstrap from a template.";
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
            MessageBox.Show("Pick a config folder first.", "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string samplesDir = BundledSamplesDir();
        if (!Directory.Exists(samplesDir))
        {
            MessageBox.Show($"Bundled samples directory not found:\n{samplesDir}", "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var templates = Directory.EnumerateFiles(samplesDir, "*.json").OrderBy(p => p).ToList();
        if (templates.Count == 0)
        {
            MessageBox.Show("No bundled samples found.", "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            _txtStatus.Text = $"Created {Path.GetFileName(target)} from template. Edit fields and Save.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("Pick a configuration first.", "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(ex.Message, "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSave()
    {
        if (Config is null || string.IsNullOrEmpty(ConfigPath))
        {
            MessageBox.Show("Pick a configuration first.", "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(ex.Message, "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveAs()
    {
        if (Config is null) { MessageBox.Show("Pick or create a configuration first.", "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Information); return; }
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
            MessageBox.Show(ex.Message, "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (UseBrief)
        {
            BriefPath = (_txtBrief.Text ?? "").Trim();
            if (string.IsNullOrEmpty(BriefPath) || !File.Exists(BriefPath))
            {
                MessageBox.Show("Browse to a valid JSON brief file, or uncheck 'Use per-wall JSON brief'.",
                    "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            FolderPath = _txtFolder.Text;
            DialogResult = true;
            return;
        }

        if (Config is null)
        {
            MessageBox.Show("Pick or create a configuration first, then Run.", "Wall Reinforcement", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!CollectAll()) return;
        FolderPath = _txtFolder.Text;
        DialogResult = true;
    }

    private static string BundledSamplesDir() =>
        Path.Combine(Path.GetDirectoryName(typeof(WallReinforcementDialog).Assembly.Location)!, "samples");
}

/// <summary>Tiny modal listbox that returns the chosen template path.</summary>
internal class TemplatePickerDialog : Window
{
    private readonly ListBox _list;
    public string? Chosen { get; private set; }

    public TemplatePickerDialog(IEnumerable<string> templates)
    {
        Title = "Pick a template";
        Width = 480;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list = new ListBox { Margin = new Thickness(8) };
        foreach (var t in templates) _list.Items.Add(t);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        var ok = new Button { Content = "Use template", IsDefault = true, MinWidth = 100, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 0, 0) };
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

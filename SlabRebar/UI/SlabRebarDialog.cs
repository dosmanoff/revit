using Autodesk.Revit.DB;
using SlabRebar.Engine;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WpfBinding = System.Windows.Data.Binding;
using WpfColor   = System.Windows.Media.Color;
using WpfGrid    = System.Windows.Controls.Grid;

namespace SlabRebar.UI;

public class SlabRebarDialog : Window
{
    private readonly Document         _doc;
    private readonly IList<ElementId> _ids;
    private List<RebarItem> _items = [];
    private bool _suppressRefresh;

    private TextBlock _txtSelectionInfo = null!;
    private TextBox   _txtBotX          = null!;
    private TextBox   _txtBotY          = null!;
    private TextBox   _txtTopX          = null!;
    private TextBox   _txtTopY          = null!;
    private ComboBox  _cmbTargetParam   = null!;
    private DataGrid  _elementsGrid     = null!;

    public ClassificationConfig   Config       { get; private set; }
    public bool                   NeedReselect { get; private set; }
    public IEnumerable<RebarItem> Items        => _items;

    public SlabRebarDialog(Document doc, IList<ElementId> ids, ClassificationConfig config)
    {
        _doc   = doc;
        _ids   = ids;
        Config = config;

        Title                 = "Slab Rebar Classifier";
        Width                 = 700;
        Height                = 560;
        MinWidth              = 580;
        MinHeight             = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUI();
        Loaded += Window_Loaded;
    }

    // ── UI construction ──────────────────────────────────────────────────────

    private void BuildUI()
    {
        var root = new WpfGrid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        UIElement[] rows =
        [
            BuildSelectionRow(),
            BuildLabelsRow(),
            BuildParameterRow(),
            BuildElementsGrid(),
            BuildButtonRow(),
        ];

        for (int i = 0; i < rows.Length; i++)
        {
            WpfGrid.SetRow(rows[i], i);
            root.Children.Add(rows[i]);
        }

        Content = root;
    }

    private UIElement BuildSelectionRow()
    {
        _txtSelectionInfo = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight        = FontWeights.SemiBold,
        };

        var reselect = new Button
        {
            Content = "Re-select…",
            Padding = new Thickness(10, 3, 10, 3),
            Margin  = new Thickness(4, 0, 0, 0),
            ToolTip = "Close dialog and pick rebar again in the model",
        };
        reselect.Click += ReSelect_Click;
        DockPanel.SetDock(reselect, Dock.Right);

        var dock = new DockPanel();
        dock.Children.Add(reselect);
        dock.Children.Add(_txtSelectionInfo);

        return MakeGroupBox("Selection", dock);
    }

    private UIElement BuildLabelsRow()
    {
        _txtBotX = MakeTextBox(72, Config.LabelBottomX);
        _txtBotY = MakeTextBox(72, Config.LabelBottomY);
        _txtTopX = MakeTextBox(72, Config.LabelTopX);
        _txtTopY = MakeTextBox(72, Config.LabelTopY);

        _txtBotX.TextChanged += Labels_Changed;
        _txtBotY.TextChanged += Labels_Changed;
        _txtTopX.TextChanged += Labels_Changed;
        _txtTopY.TextChanged += Labels_Changed;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(MakeBoldLabel("Bottom:"));
        row.Children.Add(MakeLabel("X"));  row.Children.Add(_txtBotX);
        row.Children.Add(MakeLabel("Y"));  row.Children.Add(_txtBotY);
        row.Children.Add(new Border { Width = 20 });
        row.Children.Add(MakeBoldLabel("Top:"));
        row.Children.Add(MakeLabel("X"));  row.Children.Add(_txtTopX);
        row.Children.Add(MakeLabel("Y"));  row.Children.Add(_txtTopY);

        return MakeGroupBox("Zone Labels  (value written to parameter)", row);
    }

    private UIElement BuildParameterRow()
    {
        _cmbTargetParam = new ComboBox
        {
            Width             = 220,
            IsEditable        = true,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(MakeLabel("Write to:"));
        row.Children.Add(_cmbTargetParam);

        return MakeGroupBox("Target Parameter", row);
    }

    private UIElement BuildElementsGrid()
    {
        _elementsGrid = new DataGrid
        {
            AutoGenerateColumns      = false,
            CanUserAddRows           = false,
            CanUserDeleteRows        = false,
            SelectionMode            = DataGridSelectionMode.Extended,
            GridLinesVisibility      = DataGridGridLinesVisibility.Horizontal,
            AlternatingRowBackground = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF5, 0xF5)),
            HeadersVisibility        = DataGridHeadersVisibility.Column,
            Margin                   = new Thickness(0, 0, 0, 8),
        };

        // Checkbox column
        var cbTemplate = new DataTemplate();
        var cbFactory  = new FrameworkElementFactory(typeof(CheckBox));
        cbFactory.SetBinding(CheckBox.IsCheckedProperty,
            new WpfBinding(nameof(RebarItem.IsIncluded))
            {
                Mode                = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
        cbFactory.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cbFactory.SetValue(CheckBox.VerticalAlignmentProperty,   VerticalAlignment.Center);
        cbFactory.AddHandler(CheckBox.CheckedEvent,   new RoutedEventHandler(InclusionChanged));
        cbFactory.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(InclusionChanged));
        cbTemplate.VisualTree = cbFactory;

        _elementsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header        = "✓",
            Width         = new DataGridLength(32),
            CanUserSort   = false,
            CanUserResize = false,
            CellTemplate  = cbTemplate,
        });

        _elementsGrid.Columns.Add(TextCol("Host",          nameof(RebarItem.HostName),     140));
        _elementsGrid.Columns.Add(TextCol("Zone",          nameof(RebarItem.Zone),          70));
        _elementsGrid.Columns.Add(TextCol("Direction",     nameof(RebarItem.Direction),     70));
        _elementsGrid.Columns.Add(TextCol("Current Value", nameof(RebarItem.CurrentValue), 110));

        var greenBrush = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x6B, 0x1A));
        var labelStyle = new Style(typeof(TextBlock));
        labelStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        labelStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, greenBrush));

        _elementsGrid.Columns.Add(new DataGridTextColumn
        {
            Header       = "Proposed Label",
            Binding      = new WpfBinding(nameof(RebarItem.ProposedLabel)),
            Width        = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly   = true,
            ElementStyle = labelStyle,
        });

        return _elementsGrid;
    }

    private UIElement BuildButtonRow()
    {
        var ok = new Button
        {
            Content   = "OK",
            IsDefault = true,
            MinWidth  = 80,
            Padding   = new Thickness(12, 3, 12, 3),
            Margin    = new Thickness(4, 0, 0, 0),
        };
        ok.Click += Ok_Click;

        var cancel = new Button
        {
            Content  = "Cancel",
            IsCancel = true,
            MinWidth = 80,
            Padding  = new Thickness(12, 3, 12, 3),
            Margin   = new Thickness(4, 0, 0, 0),
        };

        var panel = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }

    // ── Initialisation ───────────────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressRefresh = true;

        _txtSelectionInfo.Text = $"{_ids.Count} rebar element(s) selected";

        var writableParams = RebarClassifier.GetWritableStringParams(_doc, _ids);
        _cmbTargetParam.ItemsSource = writableParams;
        _cmbTargetParam.Text        = Config.TargetParameter;

        _suppressRefresh = false;
        RebuildItems();
    }

    // ── Item list ────────────────────────────────────────────────────────────

    private void RebuildItems()
    {
        if (_suppressRefresh) return;

        var classifier = new RebarClassifier(_doc);
        _items = classifier.BuildItems(_ids);

        foreach (RebarItem item in _items)
            item.PropertyChanged += OnItemPropertyChanged;

        _elementsGrid.ItemsSource = null;
        _elementsGrid.ItemsSource = _items;

        RefreshLabels();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RebarItem.IsIncluded))
            RefreshLabels();
    }

    private void RefreshLabels()
    {
        if (_suppressRefresh) return;
        RebarClassifier.RefreshProposedLabels(_items, CollectConfig());
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void Labels_Changed(object sender, TextChangedEventArgs e) => RefreshLabels();

    private void InclusionChanged(object sender, RoutedEventArgs e) => RefreshLabels();

    private void ReSelect_Click(object sender, RoutedEventArgs e)
    {
        NeedReselect = true;
        Config = CollectConfig();
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count(i => i.IsIncluded) == 0)
        {
            MessageBox.Show(
                "No rebar elements are included. Select at least one.",
                "Slab Rebar Classifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Config       = CollectConfig();
        DialogResult = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private ClassificationConfig CollectConfig() => new()
    {
        LabelBottomX    = _txtBotX?.Text  ?? "Bot-X",
        LabelBottomY    = _txtBotY?.Text  ?? "Bot-Y",
        LabelTopX       = _txtTopX?.Text  ?? "Top-X",
        LabelTopY       = _txtTopY?.Text  ?? "Top-Y",
        TargetParameter = _cmbTargetParam?.Text?.Trim() ?? "Comments",
    };

    private static GroupBox MakeGroupBox(string header, UIElement content) => new()
    {
        Header  = header,
        Margin  = new Thickness(0, 0, 0, 6),
        Padding = new Thickness(8, 4, 8, 6),
        Content = content,
    };

    private static Label MakeLabel(string text) => new()
    {
        Content           = text,
        VerticalAlignment = VerticalAlignment.Center,
        Padding           = new Thickness(0),
        Margin            = new Thickness(6, 0, 4, 0),
    };

    private static Label MakeBoldLabel(string text) => new()
    {
        Content           = text,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight        = FontWeights.SemiBold,
        Padding           = new Thickness(0),
        Margin            = new Thickness(6, 0, 2, 0),
    };

    private static TextBox MakeTextBox(double width, string? text = null) => new()
    {
        Width             = width,
        Text              = text ?? string.Empty,
        Padding           = new Thickness(4, 2, 4, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static DataGridTextColumn TextCol(string header, string path, double width) =>
        new()
        {
            Header     = header,
            Binding    = new WpfBinding(path),
            Width      = new DataGridLength(width),
            IsReadOnly = true,
        };
}

using Autodesk.Revit.DB;
using AutoNumbering.Engine;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WpfBinding  = System.Windows.Data.Binding;
using WpfColor    = System.Windows.Media.Color;
using WpfGrid     = System.Windows.Controls.Grid;

namespace AutoNumbering.UI;

public class AutoNumberingDialog : Window
{
    private readonly Document _doc;
    private readonly IList<ElementId> _ids;
    private List<ElementItem> _items = [];
    private bool _suppressRefresh;

    private TextBlock _txtSelectionInfo = null!;
    private TextBox   _txtPrefix        = null!;
    private TextBox   _txtSuffix        = null!;
    private TextBox   _txtStart         = null!;
    private TextBox   _txtStep          = null!;
    private TextBox   _txtDigits        = null!;
    private TextBlock _txtPreview       = null!;
    private ComboBox  _cmbTargetParam   = null!;
    private ComboBox  _cmbSortParam     = null!;
    private ComboBox  _cmbSortDir       = null!;
    private DataGrid  _elementsGrid     = null!;

    public NumberingConfig Config { get; private set; }
    public bool NeedReselect { get; private set; }
    public IEnumerable<ElementItem> Items => _items;

    public AutoNumberingDialog(Document doc, IList<ElementId> ids, NumberingConfig config)
    {
        _doc   = doc;
        _ids   = ids;
        Config = config;

        Title  = "Auto-Numbering";
        Width  = 740;
        Height = 580;
        MinWidth  = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUI();
        Loaded += Window_Loaded;
    }

    // ── UI construction ─────────────────────────────────────────────────────

    private void BuildUI()
    {
        var grid = new WpfGrid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        UIElement[] rows =
        [
            BuildSelectionRow(),
            BuildNumberingRow(),
            BuildParametersRow(),
            BuildElementsGrid(),
            BuildButtonRow(),
        ];

        for (int i = 0; i < rows.Length; i++)
        {
            WpfGrid.SetRow(rows[i], i);
            grid.Children.Add(rows[i]);
        }

        Content = grid;
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
            Content = "Re-select Elements…",
            Padding = new Thickness(12, 3, 12, 3),
            Margin  = new Thickness(4, 0, 0, 0),
            ToolTip = "Close dialog and pick elements again in the model",
        };
        reselect.Click += ReSelect_Click;
        DockPanel.SetDock(reselect, Dock.Right);

        var dock = new DockPanel();
        dock.Children.Add(reselect);
        dock.Children.Add(_txtSelectionInfo);

        return MakeGroupBox("Selection", dock);
    }

    private UIElement BuildNumberingRow()
    {
        _txtPrefix = MakeTextBox(100);
        _txtStart  = MakeTextBox(52, "1");
        _txtStep   = MakeTextBox(52, "1");
        _txtDigits = MakeTextBox(40, "1");
        _txtSuffix = MakeTextBox(100);

        _txtPrefix.TextChanged += NumberingParam_Changed;
        _txtStart.TextChanged  += NumberingParam_Changed;
        _txtStep.TextChanged   += NumberingParam_Changed;
        _txtDigits.TextChanged += NumberingParam_Changed;
        _txtSuffix.TextChanged += NumberingParam_Changed;

        _txtPreview = new TextBlock
        {
            Margin     = new Thickness(2, 6, 0, 0),
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x88, 0x88, 0x88)),
            FontStyle  = FontStyles.Italic,
            FontSize   = 11,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(MakeLabel("Prefix:"));     row.Children.Add(_txtPrefix);
        row.Children.Add(MakeLabel("Start:"));      row.Children.Add(_txtStart);
        row.Children.Add(MakeLabel("Step:"));       row.Children.Add(_txtStep);
        row.Children.Add(MakeLabel("Min digits:")); row.Children.Add(_txtDigits);
        row.Children.Add(MakeLabel("Suffix:"));     row.Children.Add(_txtSuffix);

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(_txtPreview);

        return MakeGroupBox("Numbering", stack);
    }

    private UIElement BuildParametersRow()
    {
        _cmbTargetParam = new ComboBox { Width = 180, IsEditable = true, VerticalAlignment = VerticalAlignment.Center };
        _cmbTargetParam.SelectionChanged += TargetParam_SelectionChanged;
        _cmbTargetParam.LostFocus        += TargetParam_LostFocus;

        _cmbSortParam = new ComboBox { Width = 180, IsEditable = true, VerticalAlignment = VerticalAlignment.Center };
        _cmbSortParam.SelectionChanged += SortParam_Changed;
        _cmbSortParam.LostFocus        += SortParam_LostFocus;

        _cmbSortDir = new ComboBox { Width = 100, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _cmbSortDir.Items.Add(new ComboBoxItem { Content = "Ascending",  IsSelected = true });
        _cmbSortDir.Items.Add(new ComboBoxItem { Content = "Descending" });
        _cmbSortDir.SelectedIndex    = 0;
        _cmbSortDir.SelectionChanged += SortParam_Changed;

        var writeLabel = MakeLabel("Write to:");
        writeLabel.ToolTip = "Parameter to write the number into (must be a writable text parameter)";
        var sortLabel = MakeLabel("Sort by:");
        sortLabel.ToolTip = "Sort elements by this parameter before assigning numbers";

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(writeLabel);
        row.Children.Add(_cmbTargetParam);
        row.Children.Add(sortLabel);
        row.Children.Add(_cmbSortParam);
        row.Children.Add(_cmbSortDir);

        return MakeGroupBox("Parameters", row);
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
        _elementsGrid.Sorting += ElementsGrid_Sorting;

        var cbTemplate = new DataTemplate();
        var cbFactory  = new FrameworkElementFactory(typeof(CheckBox));
        cbFactory.SetBinding(CheckBox.IsCheckedProperty,
            new WpfBinding(nameof(ElementItem.IsIncluded))
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

        _elementsGrid.Columns.Add(TextCol("Category",      nameof(ElementItem.Category),     120));
        _elementsGrid.Columns.Add(TextCol("Family / Type", nameof(ElementItem.FamilyType),   160));
        _elementsGrid.Columns.Add(TextCol("Sort Value",    nameof(ElementItem.SortKeyValue), 130));
        _elementsGrid.Columns.Add(TextCol("Current Value", nameof(ElementItem.CurrentValue), 110));

        var greenBrush     = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x6B, 0x1A));
        var newValueStyle  = new Style(typeof(TextBlock));
        newValueStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        newValueStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, greenBrush));

        _elementsGrid.Columns.Add(new DataGridTextColumn
        {
            Header       = "New Value",
            Binding      = new WpfBinding(nameof(ElementItem.ProposedNumber)),
            Width        = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly   = true,
            ElementStyle = newValueStyle,
        });

        return _elementsGrid;
    }

    private UIElement BuildButtonRow()
    {
        var okBtn = new Button
        {
            Content   = "OK",
            IsDefault = true,
            MinWidth  = 80,
            Padding   = new Thickness(12, 3, 12, 3),
            Margin    = new Thickness(4, 0, 0, 0),
        };
        okBtn.Click += Ok_Click;

        var cancelBtn = new Button
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
        panel.Children.Add(okBtn);
        panel.Children.Add(cancelBtn);
        return panel;
    }

    // ── Initialisation ──────────────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressRefresh = true;

        _txtSelectionInfo.Text = $"{_ids.Count} element(s) selected";

        var writableParams = NumberingEngine.GetWritableStringParams(_doc, _ids);
        _cmbTargetParam.ItemsSource = writableParams;
        _cmbTargetParam.Text = Config.TargetParameter;

        var sortParams = new List<string> { "(none)" };
        sortParams.AddRange(NumberingEngine.GetAllReadableParams(_doc, _ids));
        _cmbSortParam.ItemsSource = sortParams;
        _cmbSortParam.Text = string.IsNullOrEmpty(Config.SortByParameter) ? "(none)" : Config.SortByParameter;

        _cmbSortDir.SelectedIndex = Config.SortDescending ? 1 : 0;

        _txtPrefix.Text = Config.Prefix;
        _txtSuffix.Text = Config.Suffix;
        _txtStart.Text  = Config.StartNumber.ToString();
        _txtStep.Text   = Config.Step.ToString();
        _txtDigits.Text = Config.MinDigits.ToString();

        _suppressRefresh = false;
        RebuildItems();
    }

    // ── Item list building ──────────────────────────────────────────────────

    private void RebuildItems()
    {
        if (_suppressRefresh) return;

        string sortParam   = GetSortParam();
        bool   sortDesc    = _cmbSortDir.SelectedIndex == 1;
        string targetParam = _cmbTargetParam.Text?.Trim() ?? "Mark";

        _items = NumberingEngine.BuildItems(_doc, _ids, targetParam, sortParam, sortDesc);

        foreach (ElementItem item in _items)
            item.PropertyChanged += OnItemPropertyChanged;

        _elementsGrid.ItemsSource = null;
        _elementsGrid.ItemsSource = _items;

        foreach (DataGridColumn col in _elementsGrid.Columns)
            col.SortDirection = null;

        RefreshProposedNumbers();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ElementItem.IsIncluded))
            RefreshProposedNumbers();
    }

    private void RefreshProposedNumbers()
    {
        if (!TryParseNumberingParams(out int start, out int step, out int digits))
        {
            string prefix = _txtPrefix.Text ?? string.Empty;
            string suffix = _txtSuffix.Text ?? string.Empty;
            foreach (ElementItem item in _items)
                item.ProposedNumber = item.IsIncluded ? $"{prefix}?{suffix}" : string.Empty;
            _txtPreview.Text = "Preview: (invalid numbering params)";
            return;
        }

        string pfx = _txtPrefix.Text ?? string.Empty;
        string sfx = _txtSuffix.Text ?? string.Empty;

        int current = start;
        foreach (ElementItem item in _items)
        {
            if (item.IsIncluded)
            {
                item.ProposedNumber = $"{pfx}{current.ToString().PadLeft(digits, '0')}{sfx}";
                current += step;
            }
            else
            {
                item.ProposedNumber = string.Empty;
            }
        }

        string first  = $"{pfx}{start.ToString().PadLeft(digits, '0')}{sfx}";
        string second = $"{pfx}{(start + step).ToString().PadLeft(digits, '0')}{sfx}";
        _txtPreview.Text = $"Preview: {first}  →  {second}  →  …";
    }

    // ── Event handlers ──────────────────────────────────────────────────────

    private void NumberingParam_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressRefresh || _items.Count == 0) return;
        RefreshProposedNumbers();
    }

    private void TargetParam_SelectionChanged(object sender, SelectionChangedEventArgs e) => RebuildItems();
    private void TargetParam_LostFocus(object sender, RoutedEventArgs e)                  => RebuildItems();
    private void SortParam_Changed(object sender, SelectionChangedEventArgs e)             => RebuildItems();
    private void SortParam_LostFocus(object sender, RoutedEventArgs e)                    => RebuildItems();
    private void InclusionChanged(object sender, RoutedEventArgs e)                        => RefreshProposedNumbers();

    private void ElementsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        if (e.Column is not DataGridTextColumn textCol) return;
        if (textCol.Binding is not WpfBinding binding) return;

        string propName   = binding.Path.Path;
        bool   descending = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending;

        Func<ElementItem, string> selector = GetSelector(propName);

        _items = descending
            ? _items.OrderByDescending(selector, NaturalComparer.Instance).ToList()
            : _items.OrderBy(selector, NaturalComparer.Instance).ToList();

        foreach (ElementItem item in _items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            item.PropertyChanged += OnItemPropertyChanged;
        }

        e.Column.SortDirection = descending
            ? System.ComponentModel.ListSortDirection.Descending
            : System.ComponentModel.ListSortDirection.Ascending;

        foreach (DataGridColumn col in _elementsGrid.Columns)
            if (col != e.Column) col.SortDirection = null;

        _elementsGrid.ItemsSource = null;
        _elementsGrid.ItemsSource = _items;
        RefreshProposedNumbers();
    }

    private void ReSelect_Click(object sender, RoutedEventArgs e)
    {
        NeedReselect = true;
        CollectConfig();
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseNumberingParams(out _, out _, out _))
        {
            MessageBox.Show(
                "Start, Step, and Min Digits must be positive integers.",
                "Auto-Numbering", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_items.Count(i => i.IsIncluded) == 0)
        {
            MessageBox.Show(
                "No elements are included. Select at least one element to number.",
                "Auto-Numbering", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CollectConfig();
        DialogResult = true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void CollectConfig()
    {
        TryParseNumberingParams(out int start, out int step, out int digits);
        Config = new NumberingConfig
        {
            Prefix          = _txtPrefix.Text ?? string.Empty,
            Suffix          = _txtSuffix.Text ?? string.Empty,
            TargetParameter = _cmbTargetParam.Text?.Trim() ?? "Mark",
            SortByParameter = GetSortParam(),
            SortDescending  = _cmbSortDir.SelectedIndex == 1,
            StartNumber     = start,
            Step            = step,
            MinDigits       = digits,
        };
    }

    private bool TryParseNumberingParams(out int start, out int step, out int digits)
    {
        start = 1; step = 1; digits = 1;
        if (!int.TryParse(_txtStart?.Text,  out start)  || start < 0) return false;
        if (!int.TryParse(_txtStep?.Text,   out step)   || step  < 1) return false;
        if (!int.TryParse(_txtDigits?.Text, out digits) || digits < 1) return false;
        return true;
    }

    private string GetSortParam()
    {
        string? val = _cmbSortParam.Text?.Trim();
        return string.IsNullOrEmpty(val) || val == "(none)" ? string.Empty : val;
    }

    private static Func<ElementItem, string> GetSelector(string propertyName) => propertyName switch
    {
        nameof(ElementItem.Category)       => i => i.Category,
        nameof(ElementItem.FamilyType)     => i => i.FamilyType,
        nameof(ElementItem.SortKeyValue)   => i => i.SortKeyValue,
        nameof(ElementItem.CurrentValue)   => i => i.CurrentValue,
        nameof(ElementItem.ProposedNumber) => i => i.ProposedNumber,
        _                                  => i => string.Empty,
    };

    // ── Factory helpers ──────────────────────────────────────────────────────

    private static GroupBox MakeGroupBox(string header, UIElement content) => new GroupBox
    {
        Header  = header,
        Margin  = new Thickness(0, 0, 0, 6),
        Padding = new Thickness(8, 4, 8, 6),
        Content = content,
    };

    private static Label MakeLabel(string text) => new Label
    {
        Content           = text,
        VerticalAlignment = VerticalAlignment.Center,
        Padding           = new Thickness(0),
        Margin            = new Thickness(8, 0, 4, 0),
    };

    private static TextBox MakeTextBox(double width, string? text = null) => new TextBox
    {
        Width             = width,
        Text              = text ?? string.Empty,
        Padding           = new Thickness(4, 2, 4, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static DataGridTextColumn TextCol(string header, string path, double width) =>
        new DataGridTextColumn
        {
            Header     = header,
            Binding    = new WpfBinding(path),
            Width      = new DataGridLength(width),
            IsReadOnly = true,
        };
}

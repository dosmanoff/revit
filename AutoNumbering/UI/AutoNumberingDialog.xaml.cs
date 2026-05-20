using Autodesk.Revit.DB;
using AutoNumbering.Engine;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace AutoNumbering.UI;

public partial class AutoNumberingDialog : Window
{
    private readonly Document _doc;
    private readonly IList<ElementId> _ids;
    private List<ElementItem> _items = [];
    private bool _suppressRefresh;

    public NumberingConfig Config { get; private set; }
    public bool NeedReselect { get; private set; }
    public IEnumerable<ElementItem> Items => _items;

    public AutoNumberingDialog(Document doc, IList<ElementId> ids, NumberingConfig config)
    {
        InitializeComponent();
        _doc = doc;
        _ids = ids;
        Config = config;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressRefresh = true;

        TxtSelectionInfo.Text = $"{_ids.Count} element(s) selected";

        // Populate "Write to" combo with writable string parameters.
        var writableParams = NumberingEngine.GetWritableStringParams(_doc, _ids);
        CmbTargetParam.ItemsSource = writableParams;
        CmbTargetParam.Text = Config.TargetParameter;

        // Populate "Sort by" combo with all readable parameters.
        var sortParams = new List<string> { "(none)" };
        sortParams.AddRange(NumberingEngine.GetAllReadableParams(_doc, _ids));
        CmbSortParam.ItemsSource = sortParams;
        CmbSortParam.Text = string.IsNullOrEmpty(Config.SortByParameter)
            ? "(none)"
            : Config.SortByParameter;

        CmbSortDir.SelectedIndex = Config.SortDescending ? 1 : 0;

        TxtPrefix.Text = Config.Prefix;
        TxtSuffix.Text = Config.Suffix;
        TxtStart.Text = Config.StartNumber.ToString();
        TxtStep.Text = Config.Step.ToString();
        TxtDigits.Text = Config.MinDigits.ToString();

        _suppressRefresh = false;

        RebuildItems();
    }

    // ── Item list building ──────────────────────────────────────────────────

    private void RebuildItems()
    {
        if (_suppressRefresh) return;

        string sortParam = GetSortParam();
        bool sortDesc = CmbSortDir.SelectedIndex == 1;
        string targetParam = CmbTargetParam.Text?.Trim() ?? "Mark";

        _items = NumberingEngine.BuildItems(_doc, _ids, targetParam, sortParam, sortDesc);

        foreach (ElementItem item in _items)
            item.PropertyChanged += OnItemPropertyChanged;

        ElementsGrid.ItemsSource = null;
        ElementsGrid.ItemsSource = _items;

        // Clear any previous column sort indicators after a logical re-sort.
        foreach (DataGridColumn col in ElementsGrid.Columns)
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
            string prefix = TxtPrefix.Text ?? string.Empty;
            string suffix = TxtSuffix.Text ?? string.Empty;
            foreach (ElementItem item in _items)
                item.ProposedNumber = item.IsIncluded ? $"{prefix}?{suffix}" : string.Empty;
            TxtPreview.Text = "Preview: (invalid numbering params)";
            return;
        }

        string pfx = TxtPrefix.Text ?? string.Empty;
        string sfx = TxtSuffix.Text ?? string.Empty;

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

        string first = $"{pfx}{start.ToString().PadLeft(digits, '0')}{sfx}";
        string second = $"{pfx}{(start + step).ToString().PadLeft(digits, '0')}{sfx}";
        TxtPreview.Text = $"Preview: {first}  →  {second}  →  …";
    }

    // ── Event handlers ──────────────────────────────────────────────────────

    private void NumberingParam_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressRefresh || _items.Count == 0) return;
        RefreshProposedNumbers();
    }

    private void TargetParam_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RebuildItems(); // target param change updates CurrentValue column
    }

    private void TargetParam_LostFocus(object sender, RoutedEventArgs e)
    {
        RebuildItems();
    }

    private void SortParam_Changed(object sender, SelectionChangedEventArgs e)
    {
        RebuildItems();
    }

    private void SortParam_LostFocus(object sender, RoutedEventArgs e)
    {
        RebuildItems();
    }

    private void InclusionChanged(object sender, RoutedEventArgs e)
    {
        RefreshProposedNumbers();
    }

    private void ElementsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // Manual sort so _items order (= numbering order) matches the visual order.
        e.Handled = true;

        if (e.Column is not DataGridTextColumn textCol) return;
        if (textCol.Binding is not System.Windows.Data.Binding binding) return;

        string propName = binding.Path.Path;
        bool descending = e.Column.SortDirection == ListSortDirection.Ascending;

        Func<ElementItem, string> selector = GetSelector(propName);

        _items = descending
            ? _items.OrderByDescending(selector, NaturalComparer.Instance).ToList()
            : _items.OrderBy(selector, NaturalComparer.Instance).ToList();

        // Resubscribe property-changed on reordered list.
        foreach (ElementItem item in _items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            item.PropertyChanged += OnItemPropertyChanged;
        }

        e.Column.SortDirection = descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (DataGridColumn col in ElementsGrid.Columns)
            if (col != e.Column) col.SortDirection = null;

        ElementsGrid.ItemsSource = null;
        ElementsGrid.ItemsSource = _items;
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

        int includedCount = _items.Count(i => i.IsIncluded);
        if (includedCount == 0)
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
            Prefix = TxtPrefix.Text ?? string.Empty,
            Suffix = TxtSuffix.Text ?? string.Empty,
            TargetParameter = CmbTargetParam.Text?.Trim() ?? "Mark",
            SortByParameter = GetSortParam(),
            SortDescending = CmbSortDir.SelectedIndex == 1,
            StartNumber = start,
            Step = step,
            MinDigits = digits,
        };
    }

    private bool TryParseNumberingParams(out int start, out int step, out int digits)
    {
        start = 1; step = 1; digits = 1;
        if (!int.TryParse(TxtStart?.Text, out start) || start < 0) return false;
        if (!int.TryParse(TxtStep?.Text, out step) || step < 1) return false;
        if (!int.TryParse(TxtDigits?.Text, out digits) || digits < 1) return false;
        return true;
    }

    private string GetSortParam()
    {
        string? val = CmbSortParam.Text?.Trim();
        return string.IsNullOrEmpty(val) || val == "(none)" ? string.Empty : val;
    }

    private static Func<ElementItem, string> GetSelector(string propertyName) => propertyName switch
    {
        nameof(ElementItem.Category) => i => i.Category,
        nameof(ElementItem.FamilyType) => i => i.FamilyType,
        nameof(ElementItem.SortKeyValue) => i => i.SortKeyValue,
        nameof(ElementItem.CurrentValue) => i => i.CurrentValue,
        nameof(ElementItem.ProposedNumber) => i => i.ProposedNumber,
        _ => i => string.Empty,
    };
}

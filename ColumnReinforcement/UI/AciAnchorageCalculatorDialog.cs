using ColumnReinforcement.Engine;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WpfGrid = System.Windows.Controls.Grid;

namespace ColumnReinforcement.UI;

/// <summary>
/// Standalone ACI 318-19 anchorage / lap-splice calculator. Live-updating
/// modal dialog; user picks bar size + materials + condition flags, sees
/// ℓd / ℓst / ℓdc / ℓsc immediately. No model interaction — the dialog
/// computes numbers the user then types into a JSON config.
/// </summary>
public class AciAnchorageCalculatorDialog : Window
{
    private readonly ComboBox _cmbBar;
    private readonly TextBox  _txtFc;
    private readonly TextBox  _txtFy;
    private readonly CheckBox _chkTop;
    private readonly CheckBox _chkEpoxy;
    private readonly CheckBox _chkLight;
    private readonly CheckBox _chkAdequate;
    private readonly CheckBox _chkConfined;
    private readonly TextBlock _txtLd;
    private readonly TextBlock _txtLst;
    private readonly TextBlock _txtLdc;
    private readonly TextBlock _txtLsc;
    private readonly TextBlock _txtError;

    public AciAnchorageCalculatorDialog()
    {
        Title = "ACI 318-19 Anchorage Calculator";
        Width = 520;
        Height = 480;
        MinWidth = 460;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _cmbBar      = new ComboBox { Padding = new Thickness(4, 2, 4, 2) };
        foreach (string size in AciAnchorageCalculator.BarDiameters.Keys)
            _cmbBar.Items.Add(size);
        _cmbBar.SelectedItem = "#8";

        _txtFc      = MakeNumericBox("4000");
        _txtFy      = MakeNumericBox("60000");
        _chkTop     = new CheckBox { Content = "Top bar (ψt = 1.3)" };
        _chkEpoxy   = new CheckBox { Content = "Epoxy-coated bar (ψe = 1.5, capped so ψt·ψe ≤ 1.7)" };
        _chkLight   = new CheckBox { Content = "Lightweight concrete (λ = 0.75)" };
        _chkAdequate = new CheckBox { Content = "Adequate spacing + cover (Table 25.4.2.3 row 1)", IsChecked = true };
        _chkConfined = new CheckBox { Content = "Confined compression (ψr = 0.75, §25.4.9.3)" };

        _txtLd  = MakeOutputBlock();
        _txtLst = MakeOutputBlock();
        _txtLdc = MakeOutputBlock();
        _txtLsc = MakeOutputBlock();
        _txtError = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Firebrick,
            FontStyle  = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        BuildLayout();
        WireEvents();
        Recompute();
    }

    private void BuildLayout()
    {
        var root = new StackPanel { Margin = new Thickness(14) };

        var inputs = new GroupBox
        {
            Header = "Inputs",
            Padding = new Thickness(10, 6, 10, 6),
            Margin  = new Thickness(0, 0, 0, 8),
            Content = BuildInputsPanel(),
        };
        var outputs = new GroupBox
        {
            Header = "Lengths (inches)",
            Padding = new Thickness(10, 6, 10, 6),
            Margin  = new Thickness(0, 0, 0, 8),
            Content = BuildOutputsPanel(),
        };

        root.Children.Add(inputs);
        root.Children.Add(outputs);
        root.Children.Add(_txtError);
        root.Children.Add(new TextBlock
        {
            Text = "References: ACI 318-19 §25.4.2.3 (ℓd tension), §25.4.9.2 (ℓdc compression), §25.5.2.1 (ℓst Class B), §25.5.5 (ℓsc). " +
                   "Limited-spacing row of Table 25.4.2.3 (toggle off) uses divisors 16.7 / 13.3 instead of 25 / 20.",
            FontSize    = 10,
            Foreground  = System.Windows.Media.Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 8),
        });

        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 3, 12, 3) };
        close.Click += (_, _) => Close();
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btnPanel.Children.Add(close);
        root.Children.Add(btnPanel);

        Content = root;
    }

    private UIElement BuildInputsPanel()
    {
        var p = new StackPanel();
        p.Children.Add(LabeledRow("Bar size",      _cmbBar));
        p.Children.Add(LabeledRow("f'c (psi)",     _txtFc));
        p.Children.Add(LabeledRow("fy (psi)",      _txtFy));
        p.Children.Add(WithSpacing(_chkTop));
        p.Children.Add(WithSpacing(_chkEpoxy));
        p.Children.Add(WithSpacing(_chkLight));
        p.Children.Add(WithSpacing(_chkAdequate));
        p.Children.Add(WithSpacing(_chkConfined));
        return p;
    }

    private UIElement BuildOutputsPanel()
    {
        var p = new StackPanel();
        p.Children.Add(OutputRow("ℓd  — tension development",     _txtLd));
        p.Children.Add(OutputRow("ℓst — Class B tension splice",  _txtLst));
        p.Children.Add(OutputRow("ℓdc — compression development", _txtLdc));
        p.Children.Add(OutputRow("ℓsc — compression lap splice",  _txtLsc));
        return p;
    }

    private static UIElement LabeledRow(string label, UIElement field)
    {
        var grid = new WpfGrid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new Label { Content = label, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        WpfGrid.SetColumn(lbl, 0);   grid.Children.Add(lbl);
        WpfGrid.SetColumn(field, 1); grid.Children.Add(field);
        return grid;
    }

    private static UIElement OutputRow(string label, TextBlock value)
    {
        var grid = new WpfGrid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new Label { Content = label, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        WpfGrid.SetColumn(lbl, 0);   grid.Children.Add(lbl);
        WpfGrid.SetColumn(value, 1); grid.Children.Add(value);
        return grid;
    }

    private static UIElement WithSpacing(UIElement e)
    {
        if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 2, 0, 2);
        return e;
    }

    private static TextBox MakeNumericBox(string text) => new()
    {
        Text = text,
        Padding = new Thickness(4, 2, 4, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock MakeOutputBlock() => new()
    {
        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void WireEvents()
    {
        _cmbBar.SelectionChanged += (_, _) => Recompute();
        _txtFc.TextChanged       += (_, _) => Recompute();
        _txtFy.TextChanged       += (_, _) => Recompute();
        _chkTop.Checked          += (_, _) => Recompute();   _chkTop.Unchecked      += (_, _) => Recompute();
        _chkEpoxy.Checked        += (_, _) => Recompute();   _chkEpoxy.Unchecked    += (_, _) => Recompute();
        _chkLight.Checked        += (_, _) => Recompute();   _chkLight.Unchecked    += (_, _) => Recompute();
        _chkAdequate.Checked     += (_, _) => Recompute();   _chkAdequate.Unchecked += (_, _) => Recompute();
        _chkConfined.Checked     += (_, _) => Recompute();   _chkConfined.Unchecked += (_, _) => Recompute();
    }

    private void Recompute()
    {
        try
        {
            if (!double.TryParse(_txtFc.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fc))
            { ShowError("f'c is not a valid number."); return; }
            if (!double.TryParse(_txtFy.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fy))
            { ShowError("fy is not a valid number."); return; }
            if (_cmbBar.SelectedItem is not string size)
            { ShowError("Pick a bar size."); return; }

            var inputs = new AciAnchorageCalculator.Inputs
            {
                BarSize             = size,
                FcPsi               = fc,
                FyPsi               = fy,
                IsTopBar            = _chkTop.IsChecked       == true,
                IsEpoxyCoated       = _chkEpoxy.IsChecked     == true,
                IsLightweight       = _chkLight.IsChecked     == true,
                AdequateSpacing     = _chkAdequate.IsChecked  == true,
                ConfinedCompression = _chkConfined.IsChecked  == true,
            };

            var r = AciAnchorageCalculator.Compute(inputs);

            _txtLd.Text  = $"{r.DevelopmentLengthTensionIn:0.##}″";
            _txtLst.Text = $"{r.TensionLapSpliceClassBIn:0.##}″";
            _txtLdc.Text = $"{r.DevelopmentLengthCompressionIn:0.##}″";
            _txtLsc.Text = $"{r.CompressionLapSpliceIn:0.##}″";
            ClearError();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string msg)
    {
        _txtError.Text = msg;
        _txtLd.Text = _txtLst.Text = _txtLdc.Text = _txtLsc.Text = "—";
    }

    private void ClearError() => _txtError.Text = "";
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RevitActionRecorder.Snapshot;

/// <summary>
/// Окно прогресса долгих операций с кнопкой отмены.
///
/// Живёт на СОБСТВЕННОМ STA-потоке: снапшот занимает главный поток Revit минутами, и окно
/// на том же потоке Windows помечает как «(Not Responding)» — пользователь видит зависание.
/// Здесь окно перерисовывается и реагирует на кнопку всегда; рабочий поток лишь выставляет
/// текст и читает флаг отмены (проверяя его в своих контрольных точках).
/// </summary>
internal sealed class ProgressWindow
{
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile bool _cancelRequested;
    private Window? _window;
    private TextBlock? _text;
    private ProgressBar? _bar;
    private Dispatcher? _dispatcher;
    private Thread? _thread;

    public bool CancelRequested => _cancelRequested;

    public ProgressWindow(string title, IntPtr ownerHandle)
    {
        _thread = new Thread(() => RunUi(title))
        {
            IsBackground = true,
            Name = "RAR progress UI",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void RunUi(string title)
    {
        try
        {
            _text = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            _bar = new ProgressBar { Height = 18, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 0, 0, 12) };
            var cancel = new Button { Content = "Отмена", Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
            cancel.Click += (_, _) =>
            {
                _cancelRequested = true;
                cancel.IsEnabled = false;
                cancel.Content = "Отменяю…";
            };

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(_text);
            panel.Children.Add(_bar);
            panel.Children.Add(cancel);

            _window = new Window
            {
                Title = title,
                Width = 460,
                Height = 160,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Topmost = true,
                Content = panel,
            };

            _dispatcher = Dispatcher.CurrentDispatcher;
            _window.Show();
            _ready.Set();
            Dispatcher.Run();
        }
        catch
        {
            _ready.Set();
        }
    }

    public void Show()
    {
        // Окно уже показано своим потоком; метод оставлен для симметрии вызова.
    }

    public void Report(string message, double? fraction)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (_text is not null) _text.Text = message;
            if (_bar is null) return;
            if (fraction is { } f)
            {
                _bar.IsIndeterminate = false;
                _bar.Value = Math.Clamp(f, 0, 1);
            }
            else
            {
                _bar.IsIndeterminate = true;
            }
        });
    }

    public void Close()
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null) return;
        try
        {
            dispatcher.Invoke(() =>
            {
                _window?.Close();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            });
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }
}

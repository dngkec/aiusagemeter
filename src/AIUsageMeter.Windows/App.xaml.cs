using System.Windows;

namespace AIUsageMeter.Windows;

public partial class App : System.Windows.Application
{
    private AppController? _controller;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, @"Local\AIUsageMeter-7C38EE73-9600-4EA3-81EB-689F36799D38", out var isFirstInstance);
        if (!isFirstInstance) { Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show("AIUsageMeter encountered an unexpected UI error. Provider credentials and responses were not logged.",
                "AIUsageMeter", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        _controller = new AppController(Dispatcher);
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

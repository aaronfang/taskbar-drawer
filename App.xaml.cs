using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading;

namespace TaskbarDrawer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _eventWaitHandle;
    private const string MutexName = "TaskbarDrawer_SingleInstance_Mutex";
    private const string EventName = "TaskbarDrawer_Toggle_Event";

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // App is already running. Signal the existing instance to toggle window.
            try
            {
                _eventWaitHandle = EventWaitHandle.OpenExisting(EventName);
                _eventWaitHandle.Set();
            }
            catch (Exception)
            {
                // Ignore
            }
            Current.Shutdown();
            return;
        }

        // First instance
        _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var thread = new Thread(WaitForToggleEvent);
        thread.IsBackground = true;
        thread.Start();

        base.OnStartup(e);
    }

    private void WaitForToggleEvent()
    {
        while (true)
        {
            if (_eventWaitHandle?.WaitOne() == true)
            {
                // Dispatch to UI thread to toggle main window
                Dispatcher.Invoke(() =>
                {
                    if (Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.ToggleVisibility();
                    }
                });
            }
        }
    }
}

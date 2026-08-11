using System.Windows;

namespace EmeraldVeil.App;

public partial class App : System.Windows.Application
{
    private const string SingletonName = @"Local\EmeraldVeil.Singleton";

    private Mutex? _singleton;
    private VeilWindow? _veilWindow;
    private VeilController? _controller;
    private TrayIconHost? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is unavailable.");
        var startAtLogin = new StartAtLoginService(executablePath);

        if (TryHandleMaintenanceCommand(e.Args, startAtLogin))
        {
            return;
        }

        _singleton = new Mutex(initiallyOwned: true, SingletonName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        var activationDelay = ReadActivationDelay(e.Args);
        _veilWindow = new VeilWindow();
        _controller = new VeilController(
            _veilWindow,
            new Win32IdleInputSource(),
            activationDelay);
        _trayIcon = new TrayIconHost(_controller, startAtLogin, Shutdown);
        _controller.Start();

        if (e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
        {
            _controller.RequestPreview();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        if (_controller is not null)
        {
            _controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _veilWindow?.Dispose();

        if (_singleton is not null)
        {
            try
            {
                _singleton.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _singleton.Dispose();
        }

        base.OnExit(e);
    }

    private bool TryHandleMaintenanceCommand(
        IReadOnlyCollection<string> arguments,
        StartAtLoginService startAtLogin)
    {
        try
        {
            if (arguments.Contains("--install-startup", StringComparer.OrdinalIgnoreCase))
            {
                startAtLogin.Enable();
                Shutdown(0);
                return true;
            }

            if (arguments.Contains("--remove-startup", StringComparer.OrdinalIgnoreCase))
            {
                startAtLogin.Disable();
                Shutdown(0);
                return true;
            }

            return false;
        }
        catch
        {
            Shutdown(1);
            return true;
        }
    }

    private static TimeSpan ReadActivationDelay(IEnumerable<string> arguments)
    {
        const string prefix = "--idle-seconds=";
        var value = arguments.FirstOrDefault(argument =>
            argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            return TimeSpan.FromMinutes(5);
        }

        if (!int.TryParse(value[prefix.Length..], out var seconds) || seconds is < 1 or > 86_400)
        {
            throw new ArgumentException("--idle-seconds must be between 1 and 86400.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}

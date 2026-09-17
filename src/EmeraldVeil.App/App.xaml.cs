using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using EmeraldVeil.Core;

namespace EmeraldVeil.App;

public partial class App : System.Windows.Application
{
    private const string SingletonName = @"Local\EmeraldVeil.Singleton";
    private Mutex? _singleton;
    private bool _ownsSingleton;
    private VeilWindow? _veilWindow;
    private VeilController? _controller;
    private TrayIconHost? _trayIcon;
    private LowLevelInputObserver? _inputObserver;
    private SessionCommandChannel? _commands;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!Environment.UserInteractive || System.Diagnostics.Process.GetCurrentProcess().SessionId == 0)
        {
            WriteCommandResponse("{\"status\":\"interactive-session-required\"}");
            Shutdown(2);
            return;
        }
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is unavailable.");
        var startAtLogin = new StartAtLoginService(executablePath);
        if (TryHandleMaintenanceCommand(e.Args, startAtLogin)) return;

        string? command = ReadCommand(e.Args);
        _singleton = new Mutex(initiallyOwned: true, SingletonName, out _ownsSingleton);
        if (!_ownsSingleton)
        {
            try
            {
                WriteCommandResponse(SessionCommandChannel.SendAsync(command ?? "status").GetAwaiter().GetResult());
                Shutdown(0);
            }
            catch (Exception exception)
            {
                WriteCommandResponse(JsonSerializer.Serialize(new { status = "command-unavailable", error = exception.Message }));
                Shutdown(2);
            }
            return;
        }
        if (command is "status" or "pause" or "resume")
        {
            WriteCommandResponse("{\"status\":\"not-running\"}");
            Shutdown(2);
            return;
        }

        // Read-only commands above never reconstruct settings or start a watchdog.
        if (NativeBubblesSettings.IsEnabled()) _ = NativeBubblesSettings.EnsureRuntimePolicy();
        var activationDelay = ReadActivationDelay(e.Args);
        var inputFilter = new InputActivityFilter();
        _inputObserver = new LowLevelInputObserver(inputFilter);
        _veilWindow = new VeilWindow();
        _controller = new VeilController(_veilWindow, new Win32IdleInputSource(inputFilter), activationDelay);
        _inputObserver.ImmediateDisplayHotKeyPressed += (_, _) => _ = _controller.RequestImmediateDisplayFromHotKeyAsync();
        bool inputObserverStarted = _inputObserver.TryStart();
        _controller.SetHotKeyAvailability(
            inputObserverStarted,
            inputObserverStarted ? null : "The existing low-level input observer could not be installed.");
        _trayIcon = new TrayIconHost(_controller, startAtLogin, Shutdown);
        _commands = new SessionCommandChannel(request => Dispatcher.InvokeAsync(() => HandleCommand(request)).Task);
        _ = WallpaperEngineBackground.RecoverStaleAsync();
        _controller.Start();
        if (command == "show-now") _controller.RequestImmediateDisplay();
        else if (command == "preview") _controller.RequestPreview();
    }

    private string HandleCommand(string command)
    {
        if (_controller is null) return "{\"status\":\"not-running\"}";
        switch (command)
        {
            case "status": break;
            case "preview": _controller.RequestPreview(); break;
            case "show-now": _controller.RequestImmediateDisplay(); break;
            case "pause": _controller.SetPaused(true); break;
            case "resume": _controller.SetPaused(false); break;
            default: return "{\"status\":\"unknown-command\"}";
        }
        return JsonSerializer.Serialize(_controller.ReadStatus());
    }

    private static string? ReadCommand(IEnumerable<string> arguments)
    {
        foreach (string command in new[] { "status", "pause", "resume", "show-now", "preview" })
            if (arguments.Contains("--" + command, StringComparer.OrdinalIgnoreCase)) return command;
        return null;
    }

    private static void WriteCommandResponse(string response)
    {
        // A WinExe has no console of its own; redirected callers still receive JSON.
        try
        {
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            output.WriteLine(response);
        }
        catch (IOException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _commands?.Dispose();
        _trayIcon?.Dispose();
        if (_controller is not null) _controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _veilWindow?.Dispose();
        _inputObserver?.Dispose();
        if (_singleton is not null)
        {
            if (_ownsSingleton) _singleton.ReleaseMutex();
            _singleton.Dispose();
        }
        base.OnExit(e);
    }

    private bool TryHandleMaintenanceCommand(IReadOnlyCollection<string> arguments, StartAtLoginService startAtLogin)
    {
        try
        {
            if (arguments.Contains("--install-startup", StringComparer.OrdinalIgnoreCase))
            {
                startAtLogin.Enable();
                Environment.Exit(0);
                return true;
            }
            if (arguments.Contains("--remove-startup", StringComparer.OrdinalIgnoreCase))
            {
                startAtLogin.Disable();
                Environment.Exit(0);
                return true;
            }
            return false;
        }
        catch
        {
            Environment.Exit(1);
            return true;
        }
    }

    private static TimeSpan ReadActivationDelay(IEnumerable<string> arguments)
    {
        const string prefix = "--idle-seconds=";
        var value = arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null) return TimeSpan.FromMinutes(6);
        if (!int.TryParse(value[prefix.Length..], out var seconds) || seconds is < 1 or > 86_400)
            throw new ArgumentException("--idle-seconds must be between 1 and 86400.");
        return TimeSpan.FromSeconds(seconds);
    }
}

using Microsoft.Win32;
using System.IO;

namespace EmeraldVeil.App;

internal sealed class StartAtLoginService
{
    internal const string StartupValueName = "Emerald Veil";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string OwnerKeyPath = @"Software\EmeraldVeil";

    private readonly string _executablePath;
    private readonly string _expectedCommand;

    internal StartAtLoginService(string executablePath)
    {
        _executablePath = Path.GetFullPath(executablePath);
        _expectedCommand = $"\"{_executablePath}\"";
    }

    internal bool IsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return string.Equals(
            runKey?.GetValue(StartupValueName) as string,
            _expectedCommand,
            StringComparison.OrdinalIgnoreCase);
    }

    internal void Enable()
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        var existing = runKey.GetValue(StartupValueName) as string;
        if (existing is not null &&
            !string.Equals(existing, _expectedCommand, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The HKCU Run value '{StartupValueName}' is owned by another command.");
        }

        runKey.SetValue(StartupValueName, _expectedCommand, RegistryValueKind.String);

        using (var approvedKey = Registry.CurrentUser.OpenSubKey(
                   StartupApprovedKeyPath,
                   writable: true))
        {
            approvedKey?.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }

        using var ownerKey = Registry.CurrentUser.CreateSubKey(OwnerKeyPath, writable: true);
        ownerKey.SetValue("StartupExecutable", _executablePath, RegistryValueKind.String);

        if (!IsEnabled())
        {
            throw new InvalidOperationException("The startup value did not pass read-back verification.");
        }
    }

    internal void Disable()
    {
        using (var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            var existing = runKey?.GetValue(StartupValueName) as string;
            if (existing is not null &&
                !string.Equals(existing, _expectedCommand, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The HKCU Run value '{StartupValueName}' no longer points to this executable.");
            }

            runKey?.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }

        using (var approvedKey = Registry.CurrentUser.OpenSubKey(
                   StartupApprovedKeyPath,
                   writable: true))
        {
            approvedKey?.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }

        using (var ownerKey = Registry.CurrentUser.OpenSubKey(OwnerKeyPath, writable: true))
        {
            var ownerPath = ownerKey?.GetValue("StartupExecutable") as string;
            if (string.Equals(ownerPath, _executablePath, StringComparison.OrdinalIgnoreCase))
            {
                ownerKey?.DeleteValue("StartupExecutable", throwOnMissingValue: false);
            }
        }

        if (IsEnabled())
        {
            throw new InvalidOperationException("The startup value remained enabled after removal.");
        }
    }
}

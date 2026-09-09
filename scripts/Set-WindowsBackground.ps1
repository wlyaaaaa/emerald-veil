[CmdletBinding()]
param(
    [ValidateSet('Apply', 'Verify')]
    [string]$Action = 'Apply'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Windows PowerShell supplies the .NET Framework WinRT projection. No SDK,
# package installation, resident helper, or change to the Bubbles app is needed.
if ($PSVersionTable.PSEdition -ne 'Desktop' -or -not [Environment]::Is64BitProcess) {
    $systemDirectory = if ([Environment]::Is64BitProcess) { 'System32' } else { 'Sysnative' }
    $windowsPowerShell = Join-Path $env:WINDIR "$systemDirectory\WindowsPowerShell\v1.0\powershell.exe"
    & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Action $Action
    exit $LASTEXITCODE
}

Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type -AssemblyName System.Drawing
[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
[Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime] | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDesktopWallpaper {
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
    uint GetMonitorDevicePathCount();
    [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out MonitorRect rect);
}
[StructLayout(LayoutKind.Sequential)]
struct MonitorRect { public int Left, Top, Right, Bottom; }
public sealed class MonitorBackground {
    public string devicePath;
    public string path;
    public bool connected;
}
public static class EmeraldBackgroundReadback {
    private static IDesktopWallpaper OpenDesktop() {
        return (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")));
    }
    public static void SetDesktopPath(string path) {
        var desktop = OpenDesktop();
        try {
            // IDesktopWallpaper is the supported per-monitor API. On some Windows
            // 11 builds SPI_SETDESKWALLPAPER rejects user-managed image locations
            // even though this API can read them, so do not make the redundant SPI
            // call a prerequisite for setting the common and per-monitor wallpaper.
            // NULL selects the common wallpaper. Also clear remembered per-monitor
            // choices, including a detached VDD, without changing display topology.
            desktop.SetWallpaper(null, path);
            for (uint i = 0; i < desktop.GetMonitorDevicePathCount(); i++) {
                string id = desktop.GetMonitorDevicePathAt(i);
                if (!String.Equals(desktop.GetWallpaper(id), path, StringComparison.OrdinalIgnoreCase))
                    desktop.SetWallpaper(id, path);
            }
            // A per-monitor override can clear the common path on Windows 11.
            // Reassert it after the exact monitor set so both readback surfaces
            // remain stable.
            desktop.SetWallpaper(null, path);
        } finally { Marshal.FinalReleaseComObject(desktop); }
    }
    public static MonitorBackground[] MonitorPaths() {
        var desktop = OpenDesktop();
        try {
            var monitors = new MonitorBackground[desktop.GetMonitorDevicePathCount()];
            for (uint i = 0; i < monitors.Length; i++) {
                string id = desktop.GetMonitorDevicePathAt(i);
                MonitorRect rect;
                int result = desktop.GetMonitorRECT(id, out rect);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                monitors[i] = new MonitorBackground {
                    devicePath = id, path = desktop.GetWallpaper(id),
                    connected = result == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top
                };
            }
            return monitors;
        } finally { Marshal.FinalReleaseComObject(desktop); }
    }
    public static string DesktopPath() {
        var desktop = OpenDesktop();
        try { return desktop.GetWallpaper(null); }
        finally { Marshal.FinalReleaseComObject(desktop); }
    }
    // Windows may transcode its lock-screen cache. Compare decoded pixels as
    // well as the selected source; a different encoding is not image drift.
    public static double ImageError(string expected, byte[] actual) {
        using (var source = Image.FromFile(expected))
        using (var stream = new MemoryStream(actual))
        using (var current = Image.FromStream(stream)) {
            if (Math.Abs((double)source.Width / source.Height - (double)current.Width / current.Height) > 0.01)
                return 255;
            using (var a = new Bitmap(source, 128, 72))
            using (var b = new Bitmap(current, 128, 72)) {
                double total = 0;
                for (int y = 0; y < a.Height; y++)
                    for (int x = 0; x < a.Width; x++) {
                        var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
                        total += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B);
                    }
                return total / (128 * 72 * 3);
            }
        }
    }
}
'@

$assetDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\assets'))
$selection = Get-Content -LiteralPath (Join-Path $assetDirectory 'windows-background.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$sourceImage = Join-Path $assetDirectory $selection.image
if ((Get-FileHash -LiteralPath $sourceImage -Algorithm SHA256).Hash -ne $selection.sha256) {
    throw 'The selected image does not match the project manifest.'
}
$stateDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'EmeraldVeil\windows-background'
$imageDirectory = Join-Path ([Environment]::GetFolderPath('MyPictures')) 'EmeraldVeil'
$preimagePath = Join-Path $stateDirectory 'before-first-apply.json'
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and
    $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
$asActionTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and -not $_.IsGenericMethod -and
    $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'
} | Select-Object -First 1

function Wait-WinRt {
    param($Operation, [Type]$ResultType)
    $task = if ($ResultType) { $asTask.MakeGenericMethod($ResultType).Invoke($null, @($Operation)) }
        else { $asActionTask.Invoke($null, @($Operation)) }
    if (-not $task.Wait(30000)) { throw 'Windows personalization did not finish within 30 seconds.' }
    if ($ResultType) { return $task.GetAwaiter().GetResult() }
    $null = $task.GetAwaiter().GetResult()
}

# These per-user selectors choose a fixed picture rather than a slideshow or
# Spotlight rotation. They do not alter machine policy or Wallpaper Engine.
$pictureSelectors = @(
    @{ path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers'; name = 'BackgroundType'; surface = 'desktop' },
    @{ path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; name = 'RotatingLockScreenEnabled'; surface = 'lockScreen' },
    @{ path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lock Screen'; name = 'SlideshowEnabled'; surface = 'lockScreen' }
)
function Get-PictureSelectors {
    foreach ($selector in $pictureSelectors) {
        $key = Get-Item -LiteralPath $selector.path -ErrorAction SilentlyContinue
        $present = $key -and ($key.GetValueNames() -contains $selector.name)
        [pscustomobject]@{
            path = $selector.path; name = $selector.name; surface = $selector.surface
            present = [bool]$present
            kind = if ($present) { $key.GetValueKind($selector.name).ToString() } else { $null }
            value = if ($present) { $key.GetValue($selector.name) } else { $null }
        }
    }
}

function Get-ImageHash {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
    return $null
}

function Test-InstalledImage {
    param([string]$Path)
    return $Path -and [IO.Path]::GetFullPath($Path).StartsWith(
        [IO.Path]::GetFullPath($imageDirectory).TrimEnd('\') + '\',
        [StringComparison]::OrdinalIgnoreCase) -and (Get-ImageHash $Path) -eq $selection.sha256
}

function Get-LockImageBytes {
    $random = [Windows.System.UserProfile.LockScreen]::GetImageStream()
    $stream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($random)
    $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); return ,$memory.ToArray() }
    finally { $memory.Dispose(); $stream.Dispose(); $random.Dispose() }
}

function Get-CurrentState {
    $desktopPath = [EmeraldBackgroundReadback]::DesktopPath()
    $lockUri = [Windows.System.UserProfile.LockScreen]::OriginalImageFile
    $lockPath = if ($lockUri.IsFile) { $lockUri.LocalPath } else { '' }
    $lockBytes = Get-LockImageBytes
    $lockError = [EmeraldBackgroundReadback]::ImageError($sourceImage, $lockBytes)
    $rotating = @(Get-PictureSelectors | Where-Object { -not $_.present -or $_.kind -ne 'DWord' -or $_.value -ne 0 })
    $monitors = @([EmeraldBackgroundReadback]::MonitorPaths() | ForEach-Object {
        [pscustomobject]@{
            devicePath = $_.devicePath; connected = $_.connected; path = $_.path
            matches = [bool](Test-InstalledImage $_.path)
        }
    })
    [pscustomobject]@{
        desktopPath = $desktopPath
        desktopMatches = ((Test-InstalledImage $desktopPath) -and $monitors.Count -gt 0 -and
            @($monitors | Where-Object { -not $_.matches }).Count -eq 0 -and
            @($rotating | Where-Object { $_.surface -eq 'desktop' }).Count -eq 0)
        desktopMonitors = $monitors
        lockScreenPath = $lockPath
        lockScreenMatches = ((Test-InstalledImage $lockPath) -and $lockError -le 3 -and @($rotating | Where-Object { $_.surface -eq 'lockScreen' }).Count -eq 0)
        lockScreenPixelMeanError = [Math]::Round($lockError, 4)
    }
}

function Get-MonitorPreimage {
    foreach ($monitor in [EmeraldBackgroundReadback]::MonitorPaths()) {
        $hash = Get-ImageHash $monitor.path
        $backup = $null
        if ($hash) {
            $backup = Join-Path $stateDirectory ('previous-monitor-' + $hash.Substring(0, 12) + [IO.Path]::GetExtension($monitor.path))
            if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $monitor.path -Destination $backup }
            if ((Get-ImageHash $backup) -ne $hash) { throw 'Monitor preimage verification failed.' }
        }
        [pscustomobject]@{ devicePath = $monitor.devicePath; connected = $monitor.connected; path = $monitor.path; backup = $backup }
    }
}

function Save-Preimage {
    if (Test-Path -LiteralPath $preimagePath) {
        $previous = Get-Content -LiteralPath $preimagePath -Raw -Encoding UTF8 | ConvertFrom-Json
        # Upgrade only the previously uncaptured selector fields, before this
        # script changes them for the first time. Preserve the original images.
        $upgraded = $false
        if ($previous.PSObject.Properties.Name -notcontains 'pictureSelectors') {
            $previous | Add-Member -NotePropertyName pictureSelectors -NotePropertyValue @(Get-PictureSelectors)
            $previous | Add-Member -NotePropertyName selectorsCapturedUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
            $upgraded = $true
        }
        if ($previous.PSObject.Properties.Name -notcontains 'desktopMonitors') {
            $previous | Add-Member -NotePropertyName desktopMonitors -NotePropertyValue @(Get-MonitorPreimage)
            $previous | Add-Member -NotePropertyName monitorsCapturedUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
            $upgraded = $true
        }
        if ($upgraded) {
            $temporary = Join-Path $stateDirectory ('preimage-' + [Guid]::NewGuid().ToString('N') + '.tmp')
            [IO.File]::WriteAllText($temporary, ($previous | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
            [IO.File]::Replace($temporary, $preimagePath, "$preimagePath.previous")
        }
        return
    }
    New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    $desktopPath = [EmeraldBackgroundReadback]::DesktopPath()
    $lockUri = [Windows.System.UserProfile.LockScreen]::OriginalImageFile
    $desktopBackup = $null
    if ($desktopPath -and (Test-Path -LiteralPath $desktopPath -PathType Leaf)) {
        $desktopBackup = Join-Path $stateDirectory ('previous-desktop' + [IO.Path]::GetExtension($desktopPath))
        Copy-Item -LiteralPath $desktopPath -Destination $desktopBackup
    }
    $lockBytes = Get-LockImageBytes
    $extension = if ($lockBytes[0] -eq 137 -and $lockBytes[1] -eq 80) { '.png' } else { '.jpg' }
    $lockBackup = Join-Path $stateDirectory ('previous-lock-screen' + $extension)
    [IO.File]::WriteAllBytes($lockBackup, $lockBytes)
    $desktopKey = Get-ItemProperty -LiteralPath 'HKCU:\Control Panel\Desktop'
    $preimage = [ordered]@{
        capturedUtc = [DateTime]::UtcNow.ToString('o')
        desktopPath = $desktopPath
        desktopBackup = $desktopBackup
        wallpaperStyle = $desktopKey.WallpaperStyle
        tileWallpaper = $desktopKey.TileWallpaper
        lockScreenOriginalUri = $lockUri.AbsoluteUri
        lockScreenBackup = $lockBackup
        pictureSelectors = @(Get-PictureSelectors)
        desktopMonitors = @(Get-MonitorPreimage)
    }
    $temporary = Join-Path $stateDirectory ('preimage-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($temporary, ($preimage | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $preimagePath)
}

$before = Get-CurrentState
$changes = [Collections.Generic.List[string]]::new()
if ($Action -eq 'Apply' -and (-not $before.desktopMatches -or -not $before.lockScreenMatches)) {
    Save-Preimage
    foreach ($selector in (Get-PictureSelectors | Where-Object { -not $_.present -or $_.kind -ne 'DWord' -or $_.value -ne 0 })) {
        if (-not (Test-Path -LiteralPath $selector.path)) { New-Item -Path $selector.path -Force | Out-Null }
        New-ItemProperty -LiteralPath $selector.path -Name $selector.name -PropertyType DWord -Value 0 -Force | Out-Null
    }
    # Use a new input filename when reapplying after actual drift. Windows can
    # otherwise keep an old image even though the source file has been replaced.
    New-Item -ItemType Directory -Path $imageDirectory -Force | Out-Null
    $installedImage = Join-Path $imageDirectory ('rain-' + $selection.sha256.Substring(0, 12).ToLowerInvariant() + '-' + [Guid]::NewGuid().ToString('N') + '.png')
    Copy-Item -LiteralPath $sourceImage -Destination $installedImage
    if ((Get-ImageHash $installedImage) -ne $selection.sha256) { throw 'Installed image verification failed.' }
    $file = Wait-WinRt ([Windows.Storage.StorageFile]::GetFileFromPathAsync($installedImage)) ([Windows.Storage.StorageFile])
    if (-not $before.desktopMatches) {
        [EmeraldBackgroundReadback]::SetDesktopPath($installedImage)
        $changes.Add('desktop')
    }
    if (-not $before.lockScreenMatches) {
        Wait-WinRt ([Windows.System.UserProfile.LockScreen]::SetImageFileAsync($file))
        $changes.Add('lockScreen')
    }
}

$after = Get-CurrentState
$passed = $after.desktopMatches -and $after.lockScreenMatches
[ordered]@{
    status = if ($passed) { 'verified' } else { 'drift' }
    action = $Action
    image = $selection.name
    sha256 = $selection.sha256
    changedSurfaces = @($changes.ToArray())
    desktop = [ordered]@{ matches = $after.desktopMatches; path = $after.desktopPath; monitors = @($after.desktopMonitors) }
    lockScreen = [ordered]@{
        matches = $after.lockScreenMatches
        path = $after.lockScreenPath
        decodedPixelMeanError = $after.lockScreenPixelMeanError
        secureDesktopVisualTest = 'not_performed'
    }
    wallpaperEngine = 'not_modified'
    preimage = if (Test-Path -LiteralPath $preimagePath) { $preimagePath } else { $null }
} | ConvertTo-Json -Depth 5
if (-not $passed) { exit 1 }

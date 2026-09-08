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
using System.Text;
public static class EmeraldBackgroundReadback {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint size, StringBuilder value, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint size, string value, uint flags);
    public static void SetDesktopPath(string path) {
        if (!SystemParametersInfo(0x0014, 0, path, 3))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public static string DesktopPath() {
        var path = new StringBuilder(32768);
        if (!SystemParametersInfo(0x0073, (uint)path.Capacity, path, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return path.ToString();
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
        [IO.Path]::GetFullPath($stateDirectory).TrimEnd('\') + '\',
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
    [pscustomobject]@{
        desktopPath = $desktopPath
        desktopMatches = ((Test-InstalledImage $desktopPath) -and @($rotating | Where-Object { $_.surface -eq 'desktop' }).Count -eq 0)
        lockScreenPath = $lockPath
        lockScreenMatches = ((Test-InstalledImage $lockPath) -and $lockError -le 3 -and @($rotating | Where-Object { $_.surface -eq 'lockScreen' }).Count -eq 0)
        lockScreenPixelMeanError = [Math]::Round($lockError, 4)
    }
}

function Save-Preimage {
    if (Test-Path -LiteralPath $preimagePath) {
        $previous = Get-Content -LiteralPath $preimagePath -Raw -Encoding UTF8 | ConvertFrom-Json
        # Upgrade only the previously uncaptured selector fields, before this
        # script changes them for the first time. Preserve the original images.
        if ($previous.PSObject.Properties.Name -notcontains 'pictureSelectors') {
            $previous | Add-Member -NotePropertyName pictureSelectors -NotePropertyValue @(Get-PictureSelectors)
            $previous | Add-Member -NotePropertyName selectorsCapturedUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
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
    $installedImage = Join-Path $stateDirectory ('rain-' + $selection.sha256.Substring(0, 12).ToLowerInvariant() + '-' + [Guid]::NewGuid().ToString('N') + '.png')
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
    desktop = [ordered]@{ matches = $after.desktopMatches; path = $after.desktopPath }
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

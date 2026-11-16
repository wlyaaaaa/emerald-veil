#requires -Version 7.2
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Load only verification functions; the entrypoint and Windows setters never run.
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Set-WindowsBackground.ps1'),[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'The background script does not parse.' }
foreach ($name in @('Get-ImageHash','Test-InstalledImage','Get-CurrentState','Write-BackgroundLive')) {
    $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if ($null -eq $function) { throw "Missing function: $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}
# Fixtures stand in for COM/WinRT readback, including an unavailable original URI.
Add-Type @'
public static class EmeraldBackgroundReadback {
    public static string Common, Monitor;
    public static double Error;
    public static bool Connected = true;
    public static string DesktopPath() { return Common; }
    public static object[] MonitorPaths() { return new[] { new { devicePath="fixture", connected=Connected, path=Monitor } }; }
    public static double ImageError(string source, byte[] current) { return Error; }
}
namespace Windows.System.UserProfile {
    public static class LockScreen {
        public static global::System.Uri OriginalImageFile { get { return new global::System.Uri("file:///unavailable/WallpaperEngineLockOverride.jpg"); } }
    }
}
'@
function Get-LockImageBytes { return ,[byte[]]@(1) }
function Get-PictureSelectors { return $selectors }
function Assert-State($desktop, $lockScreen, $label) {
    $state=Get-CurrentState
    if ($state.desktopMatches -ne $desktop -or $state.lockScreenMatches -ne $lockScreen) { throw "Failed: $label" }
}
$root=Join-Path ([IO.Path]::GetTempPath()) ('emerald-background-test-'+[guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($root)|Out-Null
    $sourceImage=Join-Path $root 'selected.png'; [IO.File]::WriteAllText($sourceImage,'selected image')
    $legacy=Join-Path $root 'legacy.png'; Copy-Item -LiteralPath $sourceImage -Destination $legacy
    $different=Join-Path $root 'different.png'; [IO.File]::WriteAllText($different,'different image')
    $selection=[pscustomobject]@{sha256=(Get-FileHash -LiteralPath $sourceImage).Hash}
    $imageDirectory=Join-Path $root 'Pictures'
    $selectors=@('desktop','lockScreen','lockScreen' | ForEach-Object { [pscustomobject]@{surface=$_;present=$true;kind='DWord';value=0} })
    [EmeraldBackgroundReadback]::Common=$legacy; [EmeraldBackgroundReadback]::Monitor=$legacy
    Assert-State $true $true 'identical legacy copy and decoded lock-screen cache'
    [EmeraldBackgroundReadback]::Common=$different; Assert-State $false $true 'changed common image'
    [EmeraldBackgroundReadback]::Common=$legacy; [EmeraldBackgroundReadback]::Monitor=$different
    Assert-State $false $true 'changed per-monitor image'
    [EmeraldBackgroundReadback]::Connected=$false
    Assert-State $false $true 'detached per-monitor image still checked'
    [EmeraldBackgroundReadback]::Monitor=$legacy
    Assert-State $true $true 'matching detached per-monitor image'
    [EmeraldBackgroundReadback]::Connected=$true
    [EmeraldBackgroundReadback]::Monitor=(Join-Path $root 'missing.png'); Assert-State $false $true 'missing per-monitor image'
    [EmeraldBackgroundReadback]::Monitor=$legacy; [EmeraldBackgroundReadback]::Error=20
    Assert-State $true $false 'different decoded lock-screen image'
    [EmeraldBackgroundReadback]::Error=0
    foreach ($selector in $selectors) {
        $selector.value=1; Assert-State ($selector.surface -ne 'desktop') ($selector.surface -ne 'lockScreen') 'rotation enabled'
        $selector.value=0
    }
    $LiveOutput=Join-Path $root 'live.json'; Write-BackgroundLive (Get-CurrentState); Write-BackgroundLive $null
    $live=Get-Content $LiveOutput -Raw | ConvertFrom-Json; if ($null -ne $live.metrics.background -or -not $live.note) { throw 'Live output replacement must preserve null and its reason.' }
    'Windows background: content, rotation and live-output replacement checks passed. No Windows settings changed.'
}
finally { if (Test-Path -LiteralPath $root) { Get-ChildItem -LiteralPath $root -File | Remove-Item -Force; Remove-Item -LiteralPath $root -Force } }

[CmdletBinding()]
param(
    [ValidateSet('Enable', 'Disable', 'Verify', 'Restore')]
    [string]$Action = 'Enable',

    [string]$PreimagePath = (
        Join-Path $env:LOCALAPPDATA 'EmeraldVeil\native-bubbles-preimage.json'
    ),

    [string]$LegacyExecutable = (
        Join-Path $env:LOCALAPPDATA 'Programs\EmeraldVeil\EmeraldVeil.exe'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$desktopPath = 'HKCU:\Control Panel\Desktop'
$bubblesPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Screensavers\Bubbles'
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$ownerPath = 'HKCU:\Software\EmeraldVeil'
$legacyValueName = 'Emerald Veil'
$watchdogValueName = 'Emerald Veil Native Bubbles'
$legacyOwnerName = 'StartupExecutable'
$bubblesExecutable = Join-Path $env:WINDIR 'System32\Bubbles.scr'
$expectedLegacyCommand = '"{0}"' -f ([IO.Path]::GetFullPath($LegacyExecutable))
$radiusDword = [uint32]1130000000
$timeoutSeconds = [uint32]300
$utf8NoBom = [Text.UTF8Encoding]::new($false)

# The PowerShell Registry provider treats `New-Item -Force` on an existing key
# as replacement and deletes unrelated values/subkeys. Never use it here.

if (-not ('EmeraldVeil.NativeScreenSaver' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EmeraldVeil
{
    public static class NativeScreenSaver
    {
        private const uint SpiGetScreenSaveActive = 0x0010;
        private const uint SpiSetScreenSaveActive = 0x0011;
        private const uint SpiGetScreenSaveTimeout = 0x000E;
        private const uint SpiSetScreenSaveTimeout = 0x000F;
        private const uint SpiGetScreenSaveSecure = 0x0076;
        private const uint SpiSetScreenSaveSecure = 0x0077;

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool GetParameter(
            uint action,
            uint parameter,
            ref uint value,
            uint flags);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool SetParameter(
            uint action,
            uint parameter,
            IntPtr value,
            uint flags);

        private static uint Get(uint action)
        {
            uint value = 0;
            if (!GetParameter(action, 0, ref value, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return value;
        }

        private static void Set(uint action, uint value, uint flags)
        {
            if (!SetParameter(action, value, IntPtr.Zero, flags))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public static bool GetActive() => Get(SpiGetScreenSaveActive) != 0;
        public static uint GetTimeout() => Get(SpiGetScreenSaveTimeout);
        public static bool GetSecure() => Get(SpiGetScreenSaveSecure) != 0;

        public static void SetState(bool active, uint timeout, bool secure, bool persist)
        {
            uint flags = persist ? 3u : 0u;
            Set(SpiSetScreenSaveTimeout, timeout, flags);
            Set(SpiSetScreenSaveSecure, secure ? 1u : 0u, flags);
            Set(SpiSetScreenSaveActive, active ? 1u : 0u, flags);
        }
    }
}
'@
}

function Get-RegistryValueSnapshot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{
            path = $Path
            name = $Name
            exists = $false
            kind = $null
            value = $null
        }
    }

    $key = Get-Item -LiteralPath $Path
    if ($key.GetValueNames() -notcontains $Name) {
        return [pscustomobject]@{
            path = $Path
            name = $Name
            exists = $false
            kind = $null
            value = $null
        }
    }

    $kind = [string]$key.GetValueKind($Name)
    $value = $key.GetValue(
        $Name,
        $null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames
    )
    if ($kind -eq 'Binary') {
        $value = [Convert]::ToBase64String([byte[]]$value)
    }

    [pscustomobject]@{
        path = $Path
        name = $Name
        exists = $true
        kind = $kind
        value = $value
    }
}

function Set-RegistryValueSnapshot {
    param([Parameter(Mandatory)][object]$Snapshot)

    $path = [string]$Snapshot.path
    $name = [string]$Snapshot.name
    if (-not [bool]$Snapshot.exists) {
        if (Test-Path -LiteralPath $path) {
            Remove-ItemProperty -LiteralPath $path -Name $name -ErrorAction SilentlyContinue
        }
        return
    }

    if (-not (Test-Path -LiteralPath $path)) {
        try {
            New-Item -Path $path | Out-Null
        }
        catch {
            if (-not (Test-Path -LiteralPath $path)) {
                throw
            }
        }
    }
    $kind = [string]$Snapshot.kind
    $value = $Snapshot.value
    if ($kind -eq 'Binary') {
        $value = [Convert]::FromBase64String([string]$value)
    }
    elseif ($kind -eq 'DWord') {
        $value = [uint32]$value
    }
    elseif ($kind -eq 'QWord') {
        $value = [uint64]$value
    }
    elseif ($kind -eq 'MultiString') {
        $value = [string[]]$value
    }

    New-ItemProperty `
        -LiteralPath $path `
        -Name $name `
        -PropertyType $kind `
        -Value $value `
        -Force | Out-Null
}

function Assert-StateSnapshot {
    param([Parameter(Mandatory)][object]$Snapshot)

    if ([string]$Snapshot.schema -ne 'emerald-veil.native-bubbles-preimage.v1') {
        throw 'Native Bubbles preimage schema is not supported.'
    }

    $expected = @(
        [pscustomobject]@{ path = $desktopPath; name = 'SCRNSAVE.EXE' }
        [pscustomobject]@{ path = $desktopPath; name = 'ScreenSaveTimeOut' }
        [pscustomobject]@{ path = $desktopPath; name = 'ScreenSaveActive' }
        [pscustomobject]@{ path = $desktopPath; name = 'ScreenSaverIsSecure' }
        [pscustomobject]@{ path = $bubblesPath; name = 'Radius' }
        [pscustomobject]@{ path = $runPath; name = $legacyValueName }
        [pscustomobject]@{ path = $approvedPath; name = $legacyValueName }
        [pscustomobject]@{ path = $ownerPath; name = $legacyOwnerName }
    )
    $actual = @($Snapshot.registry_values)
    if ($actual.Count -ne $expected.Count) {
        throw 'Native Bubbles preimage does not contain the expected registry value set.'
    }

    foreach ($item in $expected) {
        $matches = @($actual | Where-Object {
                [string]::Equals(
                    [string]$_.path,
                    [string]$item.path,
                    [StringComparison]::OrdinalIgnoreCase
                ) -and [string]::Equals(
                    [string]$_.name,
                    [string]$item.name,
                    [StringComparison]::OrdinalIgnoreCase
                )
            })
        if ($matches.Count -ne 1) {
            throw "Native Bubbles preimage has an invalid registry identity: $($item.path)\\$($item.name)"
        }
    }

    [void][bool]$Snapshot.runtime.active
    [void][uint32]$Snapshot.runtime.timeout_seconds
    [void][bool]$Snapshot.runtime.secure
}

function Assert-StateMatchesSnapshot {
    param([Parameter(Mandatory)][object]$Expected)

    Assert-StateSnapshot -Snapshot $Expected
    $actual = Get-StateSnapshot
    if (
        [bool]$actual.runtime.active -ne [bool]$Expected.runtime.active -or
        [uint32]$actual.runtime.timeout_seconds -ne [uint32]$Expected.runtime.timeout_seconds -or
        [bool]$actual.runtime.secure -ne [bool]$Expected.runtime.secure
    ) {
        throw 'Native Bubbles runtime state did not restore to its preimage.'
    }

    foreach ($expectedValue in @($Expected.registry_values)) {
        $actualValue = Get-RegistryValueSnapshot `
            -Path ([string]$expectedValue.path) `
            -Name ([string]$expectedValue.name)
        if ([bool]$actualValue.exists -ne [bool]$expectedValue.exists) {
            throw "Registry presence did not restore: $($expectedValue.path)\\$($expectedValue.name)"
        }
        if (-not [bool]$expectedValue.exists) {
            continue
        }
        if ([string]$actualValue.kind -ne [string]$expectedValue.kind) {
            throw "Registry kind did not restore: $($expectedValue.path)\\$($expectedValue.name)"
        }
        $actualJson = $actualValue.value | ConvertTo-Json -Compress -Depth 4
        $expectedJson = $expectedValue.value | ConvertTo-Json -Compress -Depth 4
        if ($actualJson -cne $expectedJson) {
            throw "Registry value did not restore: $($expectedValue.path)\\$($expectedValue.name)"
        }
    }
}

function Get-StateSnapshot {
    [pscustomobject]@{
        schema = 'emerald-veil.native-bubbles-preimage.v1'
        captured_utc = [DateTimeOffset]::UtcNow.ToString('O')
        runtime = [pscustomobject]@{
            active = [EmeraldVeil.NativeScreenSaver]::GetActive()
            timeout_seconds = [EmeraldVeil.NativeScreenSaver]::GetTimeout()
            secure = [EmeraldVeil.NativeScreenSaver]::GetSecure()
        }
        registry_values = @(
            Get-RegistryValueSnapshot -Path $desktopPath -Name 'SCRNSAVE.EXE'
            Get-RegistryValueSnapshot -Path $desktopPath -Name 'ScreenSaveTimeOut'
            Get-RegistryValueSnapshot -Path $desktopPath -Name 'ScreenSaveActive'
            Get-RegistryValueSnapshot -Path $desktopPath -Name 'ScreenSaverIsSecure'
            Get-RegistryValueSnapshot -Path $bubblesPath -Name 'Radius'
            Get-RegistryValueSnapshot -Path $runPath -Name $legacyValueName
            Get-RegistryValueSnapshot -Path $approvedPath -Name $legacyValueName
            Get-RegistryValueSnapshot -Path $ownerPath -Name $legacyOwnerName
        )
    }
}

function Restore-StateSnapshot {
    param([Parameter(Mandatory)][object]$Snapshot)

    Assert-StateSnapshot -Snapshot $Snapshot

    [EmeraldVeil.NativeScreenSaver]::SetState(
        [bool]$Snapshot.runtime.active,
        [uint32]$Snapshot.runtime.timeout_seconds,
        [bool]$Snapshot.runtime.secure,
        $false
    )
    foreach ($value in @($Snapshot.registry_values)) {
        Set-RegistryValueSnapshot -Snapshot $value
    }
    Assert-StateMatchesSnapshot -Expected $Snapshot
}

function Save-DurablePreimage {
    param([Parameter(Mandatory)][object]$Snapshot)

    Assert-StateSnapshot -Snapshot $Snapshot
    if (Test-Path -LiteralPath $PreimagePath) {
        $existing = Get-Content -LiteralPath $PreimagePath -Raw | ConvertFrom-Json
        Assert-StateSnapshot -Snapshot $existing
        return
    }

    $directory = Split-Path -Parent ([IO.Path]::GetFullPath($PreimagePath))
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $json = $Snapshot | ConvertTo-Json -Depth 8
    $temporaryPath = Join-Path $directory (
        '.{0}.{1}.{2}.tmp' -f (
            [IO.Path]::GetFileName($PreimagePath),
            $PID,
            [Guid]::NewGuid().ToString('N')
        )
    )
    $handle = [IO.File]::Open(
        $temporaryPath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None
    )
    try {
        $bytes = $utf8NoBom.GetBytes("$json`n")
        $handle.Write($bytes, 0, $bytes.Length)
        $handle.Flush($true)
    }
    finally {
        $handle.Dispose()
    }
    try {
        [IO.File]::Move($temporaryPath, [IO.Path]::GetFullPath($PreimagePath))
    }
    catch [IO.IOException] {
        if (-not (Test-Path -LiteralPath $PreimagePath -PathType Leaf)) {
            throw
        }
        $existing = Get-Content -LiteralPath $PreimagePath -Raw | ConvertFrom-Json
        Assert-StateSnapshot -Snapshot $existing
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Get-LegacyRunValue {
    if (-not (Test-Path -LiteralPath $runPath)) {
        return $null
    }
    $item = Get-ItemProperty `
        -LiteralPath $runPath `
        -Name $legacyValueName `
        -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }
    return $item.PSObject.Properties[$legacyValueName].Value
}

function Get-WatchdogRunValue {
    if (-not (Test-Path -LiteralPath $runPath)) {
        return $null
    }
    $item = Get-ItemProperty `
        -LiteralPath $runPath `
        -Name $watchdogValueName `
        -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }
    return $item.PSObject.Properties[$watchdogValueName].Value
}

function Disable-LegacyStartup {
    $runValue = Get-LegacyRunValue
    if ($null -eq $runValue) {
        return
    }
    if (-not [string]::Equals(
            [string]$runValue,
            $expectedLegacyCommand,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a foreign '$legacyValueName' Run value: $runValue"
    }
    Remove-ItemProperty -LiteralPath $runPath -Name $legacyValueName -ErrorAction Stop
    Remove-ItemProperty -LiteralPath $approvedPath -Name $legacyValueName -ErrorAction SilentlyContinue

    # The owner marker is shared by the old and new executable. Preserve it
    # when the new watchdog Run value is already present.
    if ($null -eq (Get-WatchdogRunValue)) {
        $owner = Get-RegistryValueSnapshot -Path $ownerPath -Name $legacyOwnerName
        if ($owner.exists -and
            [string]::Equals(
                [string]$owner.value,
                [IO.Path]::GetFullPath($LegacyExecutable),
                [StringComparison]::OrdinalIgnoreCase)) {
            Remove-ItemProperty -LiteralPath $ownerPath -Name $legacyOwnerName -ErrorAction SilentlyContinue
        }
    }
    if ($null -ne (Get-LegacyRunValue)) {
        throw "The '$legacyValueName' Run value remained after removal."
    }
}

function Get-OwnedNativeBubblesProcess {
    @(Get-Process -Name 'Bubbles.scr','Bubbles' -ErrorAction SilentlyContinue | Where-Object {
        try {
            [string]::Equals(
                [IO.Path]::GetFullPath($_.Path),
                [IO.Path]::GetFullPath($bubblesExecutable),
                [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    })
}

function Stop-NativeBubblesProcess {
    $owned = @(Get-OwnedNativeBubblesProcess)
    foreach ($process in $owned) {
        $process | Stop-Process -Force
        $process | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    }

    # Process termination is asynchronous. Poll the exact path briefly so a
    # stale process object cannot make the emergency Disable path roll back.
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if (@(Get-OwnedNativeBubblesProcess).Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 100
    }

    throw "Native Bubbles is still running: $bubblesExecutable"
}

function Set-NativeRegistryConfiguration {
    foreach ($path in @($desktopPath, $bubblesPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            try {
                New-Item -Path $path | Out-Null
            }
            catch {
                if (-not (Test-Path -LiteralPath $path)) {
                    throw
                }
            }
        }
    }
    New-ItemProperty -LiteralPath $desktopPath -Name 'SCRNSAVE.EXE' `
        -PropertyType String -Value $bubblesExecutable -Force | Out-Null
    New-ItemProperty -LiteralPath $desktopPath -Name 'ScreenSaveTimeOut' `
        -PropertyType String -Value ([string]$timeoutSeconds) -Force | Out-Null
    New-ItemProperty -LiteralPath $desktopPath -Name 'ScreenSaveActive' `
        -PropertyType String -Value '1' -Force | Out-Null
    New-ItemProperty -LiteralPath $desktopPath -Name 'ScreenSaverIsSecure' `
        -PropertyType String -Value '0' -Force | Out-Null
    New-ItemProperty -LiteralPath $bubblesPath -Name 'Radius' `
        -PropertyType DWord -Value $radiusDword -Force | Out-Null
}

function Get-NativeBubblesStatus {
    $screenSaverEntry = Get-RegistryValueSnapshot -Path $desktopPath -Name 'SCRNSAVE.EXE'
    $timeoutEntry = Get-RegistryValueSnapshot -Path $desktopPath -Name 'ScreenSaveTimeOut'
    $activeEntry = Get-RegistryValueSnapshot -Path $desktopPath -Name 'ScreenSaveActive'
    $secureEntry = Get-RegistryValueSnapshot -Path $desktopPath -Name 'ScreenSaverIsSecure'
    $radiusEntry = Get-RegistryValueSnapshot -Path $bubblesPath -Name 'Radius'
    $screenSaver = [string]$screenSaverEntry.value
    $timeout = [string]$timeoutEntry.value
    $activeRegistry = [string]$activeEntry.value
    $secureRegistry = [string]$secureEntry.value
    $radius = if ($radiusEntry.exists -and $radiusEntry.kind -eq 'DWord') {
        [uint32]$radiusEntry.value
    }
    else {
        [uint32]0
    }
    $activeRuntime = [EmeraldVeil.NativeScreenSaver]::GetActive()
    $timeoutRuntime = [EmeraldVeil.NativeScreenSaver]::GetTimeout()
    $secureRuntime = [EmeraldVeil.NativeScreenSaver]::GetSecure()

    $problems = [Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $bubblesExecutable -PathType Leaf)) {
        $problems.Add("Missing native screen saver: $bubblesExecutable")
    }
    if (-not $screenSaverEntry.exists -or $screenSaverEntry.kind -ne 'String') {
        $problems.Add('SCRNSAVE.EXE is not present as REG_SZ.')
    }
    if (-not [string]::Equals(
            $screenSaver,
            $bubblesExecutable,
            [StringComparison]::OrdinalIgnoreCase)) {
        $problems.Add('SCRNSAVE.EXE does not point to the native Bubbles screen saver.')
    }
    if ($timeout -ne [string]$timeoutSeconds -or $timeoutRuntime -ne $timeoutSeconds) {
        $problems.Add('Screen saver timeout is not 300 seconds in both registry and runtime state.')
    }
    if (-not $timeoutEntry.exists -or $timeoutEntry.kind -ne 'String') {
        $problems.Add('ScreenSaveTimeOut is not present as REG_SZ.')
    }
    if ($secureRegistry -ne '0' -or $secureRuntime) {
        $problems.Add('Screen saver exit is not configured as non-secure.')
    }
    if (-not $secureEntry.exists -or $secureEntry.kind -ne 'String') {
        $problems.Add('ScreenSaverIsSecure is not present as REG_SZ.')
    }
    if (-not $radiusEntry.exists -or $radiusEntry.kind -ne 'DWord') {
        $problems.Add('Native Bubbles Radius is not present as REG_DWORD.')
    }
    if ($radius -ne $radiusDword) {
        $problems.Add('Native Bubbles radius does not match the enlarged profile.')
    }
    if ($activeRegistry -notin @('0', '1')) {
        $problems.Add('ScreenSaveActive is not a supported registry value.')
    }
    if (-not $activeEntry.exists -or $activeEntry.kind -ne 'String') {
        $problems.Add('ScreenSaveActive is not present as REG_SZ.')
    }
    if (($activeRegistry -eq '1') -ne $activeRuntime) {
        $problems.Add('ScreenSaveActive registry and runtime state disagree.')
    }
    if ($null -ne (Get-LegacyRunValue)) {
        $problems.Add('The legacy Emerald Veil Run entry is still enabled.')
    }
    $legacyApproval = Get-RegistryValueSnapshot -Path $approvedPath -Name $legacyValueName
    $legacyOwner = Get-RegistryValueSnapshot -Path $ownerPath -Name $legacyOwnerName
    if ($legacyApproval.exists -or ($legacyOwner.exists -and $null -eq (Get-WatchdogRunValue))) {
        $problems.Add('Legacy Emerald Veil startup metadata is still present.')
    }
    if ($problems.Count -gt 0) {
        throw ($problems -join [Environment]::NewLine)
    }

    [pscustomobject]@{
        status = if ($activeRuntime) { 'enabled' } else { 'disabled' }
        screen_saver = $bubblesExecutable
        timeout_seconds = [int]$timeoutRuntime
        secure = $secureRuntime
        radius_dword = [uint32]$radius
        radius_float = [BitConverter]::ToSingle(
            [BitConverter]::GetBytes([uint32]$radius),
            0
        )
        legacy_startup_enabled = $false
        preimage = [IO.Path]::GetFullPath($PreimagePath)
    }
}

function Get-NativeBubblesDisabledStatus {
    $activeEntry = Get-RegistryValueSnapshot -Path $desktopPath -Name 'ScreenSaveActive'
    $activeRuntime = [EmeraldVeil.NativeScreenSaver]::GetActive()
    if (
        -not $activeEntry.exists -or
        $activeEntry.kind -ne 'String' -or
        [string]$activeEntry.value -ne '0' -or
        $activeRuntime
    ) {
        throw 'Native Bubbles did not enter the disabled state.'
    }

    [pscustomobject]@{
        status = 'disabled'
        screen_saver = $bubblesExecutable
        automatic_start = $false
        preimage = [IO.Path]::GetFullPath($PreimagePath)
    }
}

switch ($Action) {
    'Enable' {
        if (-not (Test-Path -LiteralPath $bubblesExecutable -PathType Leaf)) {
            throw "Native Bubbles screen saver is missing: $bubblesExecutable"
        }
        $operationPreimage = Get-StateSnapshot
        Save-DurablePreimage -Snapshot $operationPreimage
        try {
            Disable-LegacyStartup
            Set-NativeRegistryConfiguration
            [EmeraldVeil.NativeScreenSaver]::SetState($true, $timeoutSeconds, $false, $true)
            Get-NativeBubblesStatus
        }
        catch {
            Restore-StateSnapshot -Snapshot $operationPreimage
            throw
        }
        break
    }

    'Disable' {
        $operationPreimage = Get-StateSnapshot
        try {
            if (-not (Test-Path -LiteralPath $desktopPath)) {
                throw 'The desktop configuration key is missing.'
            }
            Stop-NativeBubblesProcess
            New-ItemProperty -LiteralPath $desktopPath -Name 'ScreenSaveActive' `
                -PropertyType String -Value '0' -Force | Out-Null
            [EmeraldVeil.NativeScreenSaver]::SetState(
                $false,
                [EmeraldVeil.NativeScreenSaver]::GetTimeout(),
                [EmeraldVeil.NativeScreenSaver]::GetSecure(),
                $true
            )
            Get-NativeBubblesDisabledStatus
        }
        catch {
            Restore-StateSnapshot -Snapshot $operationPreimage
            throw
        }
        break
    }

    'Verify' {
        Get-NativeBubblesStatus
        break
    }

    'Restore' {
        if (-not (Test-Path -LiteralPath $PreimagePath -PathType Leaf)) {
            throw "Native Bubbles preimage is missing: $PreimagePath"
        }
        $operationPreimage = Get-StateSnapshot
        try {
            $preimage = Get-Content -LiteralPath $PreimagePath -Raw | ConvertFrom-Json
            Restore-StateSnapshot -Snapshot $preimage
            [pscustomobject]@{
                status = 'restored'
                preimage = [IO.Path]::GetFullPath($PreimagePath)
            }
        }
        catch {
            Restore-StateSnapshot -Snapshot $operationPreimage
            throw
        }
    }
}

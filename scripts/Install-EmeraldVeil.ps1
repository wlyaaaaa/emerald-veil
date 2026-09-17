[CmdletBinding()]
param(
    [ValidateSet('Install', 'Verify', 'Remove')]
    [string]$Action = 'Install',

    [string]$SourcePath = (Join-Path $PSScriptRoot '..\artifacts\publish\win-x64\EmeraldVeil.exe'),

    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\EmeraldVeil')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) {
    throw 'Run this per-user desktop entry in the signed-in interactive session, not SYSTEM/session 0.'
}

$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$ownerKeyPath = 'HKCU:\Software\EmeraldVeil'
$valueName = 'Emerald Veil Native Bubbles'
$legacyValueName = 'Emerald Veil'
$targetPath = Join-Path ([System.IO.Path]::GetFullPath($InstallDirectory)) 'EmeraldVeil.exe'
$expectedCommand = '"{0}"' -f $targetPath

function Get-RunValue {
    if (-not (Test-Path -LiteralPath $runKeyPath)) {
        return $null
    }

    $item = Get-ItemProperty -LiteralPath $runKeyPath -Name $valueName -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }

    return $item.PSObject.Properties[$valueName].Value
}

function Get-LegacyRunValue {
    if (-not (Test-Path -LiteralPath $runKeyPath)) {
        return $null
    }
    $item = Get-ItemProperty -LiteralPath $runKeyPath -Name $legacyValueName -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }
    return $item.PSObject.Properties[$legacyValueName].Value
}

function Remove-OwnedLegacyRunValue {
    $legacy = Get-LegacyRunValue
    if ($null -eq $legacy) {
        return
    }
    if (-not [string]::Equals($legacy, $expectedCommand, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove foreign HKCU Run value '$legacyValueName': $legacy"
    }
    Remove-ItemProperty -LiteralPath $runKeyPath -Name $legacyValueName -ErrorAction Stop
    Remove-ItemProperty -LiteralPath $approvedKeyPath -Name $legacyValueName -ErrorAction SilentlyContinue
}

function Get-OwnerPath {
    if (-not (Test-Path -LiteralPath $ownerKeyPath)) {
        return $null
    }

    $item = Get-ItemProperty -LiteralPath $ownerKeyPath -Name 'StartupExecutable' -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }

    return $item.PSObject.Properties['StartupExecutable'].Value
}

function Test-LegacyEmeraldVeilExecutable {
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        return $false
    }

    $version = (Get-Item -LiteralPath $targetPath).VersionInfo
    return [string]::Equals($version.ProductName, 'EmeraldVeil', [System.StringComparison]::Ordinal) -and
        $version.OriginalFilename -in @('EmeraldVeil.dll', 'EmeraldVeil.exe')
}

function Test-InstalledState {
    $problems = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        $problems.Add("Installed executable is missing: $targetPath")
    }

    $runValue = Get-RunValue
    if (-not [string]::Equals($runValue, $expectedCommand, [System.StringComparison]::OrdinalIgnoreCase)) {
        $problems.Add("HKCU Run value does not exactly match: $expectedCommand")
    }

    $ownerPath = Get-OwnerPath
    if (-not [string]::Equals($ownerPath, $targetPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        $problems.Add("Owner marker does not exactly match: $targetPath")
    }

    if ($problems.Count -gt 0) {
        throw ($problems -join [Environment]::NewLine)
    }

    [pscustomobject]@{
        status = 'verified'
        executable = $targetPath
        installed_version = (Get-Item -LiteralPath $targetPath).VersionInfo.FileVersion
        installed_sha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
        startup_value_name = $valueName
        startup_command = $expectedCommand
    }
}

function Test-CurrentProcessKillOnCloseJob {
    if (-not ('EmeraldVeil.InstallJobProbe' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EmeraldVeil
{
    public static class InstallJobProbe
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimitInformation { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimitInformation { public BasicLimitInformation BasicLimitInformation; public IoCounters IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(IntPtr processHandle, IntPtr jobHandle, out bool result);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(IntPtr jobHandle, int informationClass, out ExtendedLimitInformation information, int informationLength, IntPtr returnLength);
        public static bool IsKillOnCloseJob()
        {
            bool inJob;
            if (!IsProcessInJob(Process.GetCurrentProcess().Handle, IntPtr.Zero, out inJob)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!inJob) return false;
            ExtendedLimitInformation information;
            if (!QueryInformationJobObject(IntPtr.Zero, JobObjectExtendedLimitInformation, out information, Marshal.SizeOf<ExtendedLimitInformation>(), IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return (information.BasicLimitInformation.LimitFlags & JobObjectLimitKillOnJobClose) != 0;
        }
    }
}
"@
    }
    return [EmeraldVeil.InstallJobProbe]::IsKillOnCloseJob()
}

function Test-ResidentState {
    $ownedProcesses = @(Get-Process -Name 'EmeraldVeil' -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId -and
                [string]::Equals($_.Path, $targetPath, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    })
    if ($ownedProcesses.Count -ne 1) {
        throw "Expected exactly one interactive Emerald Veil resident; found $($ownedProcesses.Count)."
    }

    $probeId = [guid]::NewGuid().ToString('N')
    $stdoutPath = Join-Path $env:TEMP ("EmeraldVeil-status-$probeId.out")
    $stderrPath = Join-Path $env:TEMP ("EmeraldVeil-status-$probeId.err")
    try {
        $probe = Start-Process -FilePath $targetPath -ArgumentList '--status' -WindowStyle Hidden -Wait -PassThru `
            -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        $raw = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
        if ($probe.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) {
            $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
            throw "Emerald Veil resident status command failed with exit code $($probe.ExitCode): $stderr"
        }
        try {
            $status = ($raw | ConvertFrom-Json -Depth 20)
        }
        catch {
            throw "Emerald Veil resident returned invalid status JSON: $($_.Exception.Message)"
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath,$stderrPath -Force -ErrorAction SilentlyContinue
    }

    if ([string]$status.schema -ne 'emerald-veil.status.v1' -or
        -not [bool]$status.enabled -or
        -not [bool]$status.hotKeyAvailable -or
        [string]$status.immediateHotKey -ne 'Ctrl+Win+E') {
        throw 'Emerald Veil resident status does not satisfy the enabled hotkey contract.'
    }

    [pscustomobject]@{
        resident_status = 'healthy'
        resident_process_id = [int]$ownedProcesses[0].Id
        resident_version = [string]$status.version
        enabled = [bool]$status.enabled
        immediate_hotkey = [string]$status.immediateHotKey
        hotkey_available = [bool]$status.hotKeyAvailable
    }
}

function Stop-OwnedProcess {
    $ownedProcesses = @(Get-Process -Name 'EmeraldVeil' -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId -and
                [string]::Equals($_.Path, $targetPath, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    })

    if ($ownedProcesses.Count -eq 0) {
        return
    }

    $ownedProcesses | Stop-Process -Force
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $remaining = @($ownedProcesses | Where-Object {
            try { -not $_.HasExited }
            catch { $false }
        })
        if ($remaining.Count -eq 0) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Owned Emerald Veil process did not exit within 5 seconds: $($remaining.Id -join ',')"
}

function Start-OwnedResident {
    # The caller is already proven outside any kill-on-close job before Install.
    # Launch in the signed-in interactive user session; natural login uses the same HKCU Run.
    $shell=New-Object -ComObject Shell.Application
    try { $shell.ShellExecute($targetPath, '', (Split-Path -Parent $targetPath), 'open', 1) }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    do {
        $match=@(Get-Process -Name 'EmeraldVeil' -ErrorAction SilentlyContinue|Where-Object {
            try { $_.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId -and $_.Path -ieq $targetPath }
            catch { $false }
        })
        if($match.Count -eq 1){return}
        if($match.Count -gt 1){throw 'Multiple owned resident controllers found.'}
        Start-Sleep -Milliseconds 100
    } while([DateTime]::UtcNow -lt $deadline)
    throw 'The installed resident did not start in the signed-in session.'
}
function Remove-OwnedFile {
    param([Parameter(Mandatory)][string]$Path)

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    throw "Owned file remained after removal retries: $Path"
}

switch ($Action) {
    'Verify' {
        $installed = Test-InstalledState
        $resident = Test-ResidentState
        [pscustomobject]@{
            status = 'verified'
            installed = $installed
            resident = $resident
        }
        break
    }

    'Install' {
        if (Test-CurrentProcessKillOnCloseJob) {
            throw 'Refusing to install from a kill-on-close job: any resident child would be terminated when this installer process exits. Run the installer from a normal interactive PowerShell or an out-of-job user-session deployment broker.'
        }

        $resolvedSource = [System.IO.Path]::GetFullPath($SourcePath)
        if (-not (Test-Path -LiteralPath $resolvedSource -PathType Leaf)) {
            throw "Published executable is missing: $resolvedSource"
        }

        $existingRun = Get-RunValue
        if ($null -ne $existingRun -and
            -not [string]::Equals($existingRun, $expectedCommand, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to overwrite foreign HKCU Run value '$valueName': $existingRun"
        }

        $existingOwner = Get-OwnerPath
        if ((Test-Path -LiteralPath $targetPath -PathType Leaf) -and
            -not [string]::Equals($existingOwner, $targetPath, [System.StringComparison]::OrdinalIgnoreCase) -and
            -not (Test-LegacyEmeraldVeilExecutable)) {
            throw "Refusing to overwrite an executable without the Emerald Veil owner marker: $targetPath"
        }

        New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
        $stagedPath = Join-Path $InstallDirectory ('.EmeraldVeil.{0}.tmp' -f [guid]::NewGuid().ToString('N'))
        $backupPath = "$targetPath.previous"
        $replacementPerformed = $false
        $hadTarget = Test-Path -LiteralPath $targetPath -PathType Leaf
        $wasRunning = @(Get-Process -Name 'EmeraldVeil' -ErrorAction SilentlyContinue | Where-Object {
            try { $_.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId -and $_.Path -ieq $targetPath }
            catch { $false }
        }).Count -gt 0

        try {
            Copy-Item -LiteralPath $resolvedSource -Destination $stagedPath
            $sourceHash = (Get-FileHash -LiteralPath $resolvedSource -Algorithm SHA256).Hash
            $stagedHash = (Get-FileHash -LiteralPath $stagedPath -Algorithm SHA256).Hash
            if ($sourceHash -ne $stagedHash) {
                throw 'Staged executable hash does not match the published source.'
            }

            Stop-OwnedProcess
            if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
                [System.IO.File]::Replace($stagedPath, $targetPath, $backupPath, $true)
            }
            else {
                [System.IO.File]::Move($stagedPath, $targetPath)
            }

            $replacementPerformed = $true
            $maintenance = Start-Process -FilePath $targetPath -ArgumentList '--install-startup' -WindowStyle Hidden -Wait -PassThru
            if ($maintenance.ExitCode -ne 0) {
                throw "Startup registration failed with exit code $($maintenance.ExitCode)."
            }

            Remove-OwnedLegacyRunValue

            Test-InstalledState | Out-Null
            Start-OwnedResident
            $installed = Test-InstalledState
            $resident = Test-ResidentState
            [pscustomobject]@{
                status = 'installed-and-running'
                installed = $installed
                resident = $resident
            }
        }
        catch {
            if ($replacementPerformed -and $hadTarget -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
                Stop-OwnedProcess
                Copy-Item -LiteralPath $backupPath -Destination $targetPath -Force
                if ($wasRunning) { Start-OwnedResident }
            }
            elseif ($replacementPerformed -and -not $hadTarget) {
                Stop-OwnedProcess
                Remove-Item -LiteralPath $targetPath -Force -ErrorAction SilentlyContinue
            }
            throw
        }
        finally {
            Remove-Item -LiteralPath $stagedPath -Force -ErrorAction SilentlyContinue
        }

        break
    }

    'Remove' {
        $existingRun = Get-RunValue
        if ($null -ne $existingRun -and
            -not [string]::Equals($existingRun, $expectedCommand, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove foreign HKCU Run value '$valueName': $existingRun"
        }

        $ownerPath = Get-OwnerPath
        if ((Test-Path -LiteralPath $targetPath -PathType Leaf) -and
            -not [string]::Equals($ownerPath, $targetPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove an installation owned by another path: $ownerPath"
        }

        if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
            $maintenance = Start-Process -FilePath $targetPath -ArgumentList '--remove-startup' -WindowStyle Hidden -Wait -PassThru
            if ($maintenance.ExitCode -ne 0) {
                throw "Startup removal failed with exit code $($maintenance.ExitCode)."
            }
        }
        elseif (Test-Path -LiteralPath $runKeyPath) {
            Remove-ItemProperty -LiteralPath $runKeyPath -Name $valueName -ErrorAction SilentlyContinue
            Remove-ItemProperty -LiteralPath $approvedKeyPath -Name $valueName -ErrorAction SilentlyContinue
            Remove-ItemProperty -LiteralPath $ownerKeyPath -Name 'StartupExecutable' -ErrorAction SilentlyContinue
        }

        Remove-OwnedLegacyRunValue

        if (Test-Path -LiteralPath $ownerKeyPath) {
            Remove-ItemProperty `
                -LiteralPath $ownerKeyPath `
                -Name 'NativeBubblesEnabled' `
                -ErrorAction SilentlyContinue
        }

        Stop-OwnedProcess
        Remove-OwnedFile -Path $targetPath
        Remove-OwnedFile -Path "$targetPath.previous"

        if ($null -ne (Get-RunValue)) {
            throw "HKCU Run value '$valueName' remained after removal."
        }

        if ($null -ne (Get-LegacyRunValue)) {
            throw "Legacy HKCU Run value '$legacyValueName' remained after removal."
        }

        if ($null -ne (Get-OwnerPath)) {
            throw 'The Emerald Veil startup owner marker remained after removal.'
        }

        [pscustomobject]@{
            status = 'removed'
            executable = $targetPath
            startup_value_name = $valueName
        }
    }
}

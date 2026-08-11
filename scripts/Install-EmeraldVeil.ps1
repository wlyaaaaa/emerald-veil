[CmdletBinding()]
param(
    [ValidateSet('Install', 'Verify', 'Remove')]
    [string]$Action = 'Install',

    [string]$SourcePath = (Join-Path $PSScriptRoot '..\artifacts\publish\win-x64\EmeraldVeil.exe'),

    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\EmeraldVeil')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$ownerKeyPath = 'HKCU:\Software\EmeraldVeil'
$valueName = 'Emerald Veil'
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
        startup_value_name = $valueName
        startup_command = $expectedCommand
    }
}

function Stop-OwnedProcess {
    $ownedProcesses = @(Get-Process -Name 'EmeraldVeil' -ErrorAction SilentlyContinue | Where-Object {
        try {
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
    $ownedProcesses | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
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
        Test-InstalledState
        break
    }

    'Install' {
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
            -not [string]::Equals($existingOwner, $targetPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to overwrite an executable without the Emerald Veil owner marker: $targetPath"
        }

        New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
        $stagedPath = Join-Path $InstallDirectory ('.EmeraldVeil.{0}.tmp' -f [guid]::NewGuid().ToString('N'))
        $backupPath = "$targetPath.previous"

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

            $maintenance = Start-Process -FilePath $targetPath -ArgumentList '--install-startup' -WindowStyle Hidden -Wait -PassThru
            if ($maintenance.ExitCode -ne 0) {
                throw "Startup registration failed with exit code $($maintenance.ExitCode)."
            }

            Test-InstalledState | Out-Null
            Start-Process -FilePath $targetPath
            Test-InstalledState
        }
        catch {
            if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
                Stop-OwnedProcess
                Copy-Item -LiteralPath $backupPath -Destination $targetPath -Force
            }
            elseif (-not [string]::Equals((Get-OwnerPath), $targetPath, [System.StringComparison]::OrdinalIgnoreCase)) {
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

        Stop-OwnedProcess
        Remove-OwnedFile -Path $targetPath
        Remove-OwnedFile -Path "$targetPath.previous"

        if ($null -ne (Get-RunValue)) {
            throw "HKCU Run value '$valueName' remained after removal."
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

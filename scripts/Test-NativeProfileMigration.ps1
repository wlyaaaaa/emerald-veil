#requires -Version 7.2
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Extract only the two pure/file-backed preimage functions. Never dot-source the
# product entrypoint, load a renderer, write HKCU, or invoke its Action switch.
$tokens=$null; $errors=$null
$source=Join-Path $PSScriptRoot 'Set-NativeBubbles.ps1'
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'The native profile script does not parse.' }
foreach ($name in @('Assert-StateSnapshot','Save-DurablePreimage')) {
    $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if ($null -eq $function) { throw "Missing function: $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}
$desktopPath='fixture:desktop'; $bubblesPath='fixture:bubbles'
$runPath='fixture:run'; $approvedPath='fixture:approval'; $ownerPath='fixture:owner'
$legacyValueName='Legacy'; $legacyOwnerName='LegacyOwner'; $watchdogEnabledName='Enabled'
$utf8NoBom=[Text.UTF8Encoding]::new($false)
function New-Fixture {
    $rows=@(
        [pscustomobject]@{path=$desktopPath;name='SCRNSAVE.EXE';exists=$true;kind='String';value='original.scr'},
        [pscustomobject]@{path=$desktopPath;name='ScreenSaveTimeOut';exists=$true;kind='String';value='721'},
        [pscustomobject]@{path=$desktopPath;name='ScreenSaveActive';exists=$false;kind=$null;value=$null},
        [pscustomobject]@{path=$desktopPath;name='ScreenSaverIsSecure';exists=$true;kind='String';value='1'},
        [pscustomobject]@{path=$bubblesPath;name='Radius';exists=$true;kind='DWord';value=9},
        [pscustomobject]@{path=$runPath;name=$legacyValueName;exists=$false;kind=$null;value=$null},
        [pscustomobject]@{path=$approvedPath;name=$legacyValueName;exists=$false;kind=$null;value=$null},
        [pscustomobject]@{path=$ownerPath;name=$legacyOwnerName;exists=$false;kind=$null;value=$null},
        [pscustomobject]@{path=$ownerPath;name=$watchdogEnabledName;exists=$false;kind=$null;value=$null},
        [pscustomobject]@{path=$bubblesPath;name='ShowBubbles';exists=$true;kind='DWord';value=17},
        [pscustomobject]@{path=$bubblesPath;name='MaterialGlass';exists=$true;kind='DWord';value=23}
    )
    [pscustomobject]@{schema='emerald-veil.native-bubbles-preimage.v4';captured_utc='2020-01-01T00:00:00Z';runtime=[pscustomobject]@{active=$true;timeout_seconds=721;secure=$true};registry_values=$rows}
}
$root=Join-Path ([IO.Path]::GetTempPath()) ('emerald-profile-test-'+[guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($root)|Out-Null
    $PreimagePath=Join-Path $root 'v4.json'
    $before=New-Fixture
    [IO.File]::WriteAllText($PreimagePath,($before|ConvertTo-Json -Depth 8),$utf8NoBom)
    $hash=(Get-FileHash $PreimagePath).Hash
    $changed=New-Fixture; $changed.runtime.timeout_seconds=999
    foreach ($attempt in 1..2) {
        Save-DurablePreimage -Snapshot $changed
        if ((Get-FileHash $PreimagePath).Hash -cne $hash) { throw 'Repeated Enable rewrote the durable original' }
    }
    $PreimagePath=Join-Path $root 'fresh.json'
    Save-DurablePreimage -Snapshot (New-Fixture)
    Assert-StateSnapshot (Get-Content $PreimagePath -Raw|ConvertFrom-Json)
    [IO.File]::WriteAllText($PreimagePath,'{"schema":"unexpected"}',$utf8NoBom)
    $hash=(Get-FileHash $PreimagePath).Hash; $rejected=$false
    try { Save-DurablePreimage -Snapshot (New-Fixture) } catch { $rejected=$true }
    if (-not $rejected -or (Get-FileHash $PreimagePath).Hash -cne $hash) { throw 'Malformed preimage was not preserved and rejected' }
    'Native v4 preimage: idempotence, fresh install and malformed-original tests passed. No registry or desktop changes.'
}
finally { if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force } }

# SPDX-License-Identifier: GPL-3.0-only
# Run after the normal Tauri --no-bundle build.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $CargoTargetDirectory,
    [Parameter(Mandatory = $true)][string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configPath = Join-Path $repo 'apps/desktop/src-tauri/tauri.conf.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$version = [string]$config.version

$mainExe = Join-Path $CargoTargetDirectory 'release/km-editor-desktop.exe'
$bridgeExe = Join-Path $repo 'apps/desktop/src-tauri/binaries/km-tools-bridge-x86_64-pc-windows-msvc.exe'
foreach ($path in @($mainExe, $bridgeExe)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required portable input is missing: $path"
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stageRoot = Join-Path $OutputDirectory ('stage-' + [guid]::NewGuid().ToString('N'))
$packageDirectory = Join-Path $stageRoot 'KM.Editor.Portable'
$zipPath = Join-Path $OutputDirectory "KM.Editor.Portable_${version}_x64.zip"
try {
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    Copy-Item -LiteralPath $mainExe -Destination (Join-Path $packageDirectory 'km-editor-desktop.exe')
    Copy-Item -LiteralPath $bridgeExe -Destination (Join-Path $packageDirectory 'km-tools-bridge.exe')
    Set-Content -LiteralPath (Join-Path $packageDirectory 'portable.flag') -Value 'KM Editor portable data mode' -Encoding ascii
    New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'Data') -Force | Out-Null

    if (Test-Path -LiteralPath $zipPath) {
        throw "Refusing to overwrite existing archive: $zipPath"
    }
    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Created portable archive: $zipPath"
    Get-FileHash -LiteralPath $zipPath -Algorithm SHA256 | Format-Table
} finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}

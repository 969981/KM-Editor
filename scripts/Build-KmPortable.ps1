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
# Windows upstream now publishes a self-contained multi-file .NET sidecar.
# The Rust release launcher resolves <desktop exe dir>/bridge/km-tools-bridge.exe.
$bridgeDirectory = Join-Path $repo 'apps/desktop/src-tauri/binaries/bridge/x86_64-pc-windows-msvc'
$bridgeExe = Join-Path $bridgeDirectory 'km-tools-bridge.exe'
$bridgeRequiredFiles = @(
    $bridgeExe,
    (Join-Path $bridgeDirectory 'KM.Tools.dll'),
    (Join-Path $bridgeDirectory 'KM.Tools.deps.json'),
    (Join-Path $bridgeDirectory 'KM.Tools.runtimeconfig.json'),
    (Join-Path $bridgeDirectory 'hostfxr.dll'),
    (Join-Path $bridgeDirectory 'hostpolicy.dll'),
    (Join-Path $bridgeDirectory 'coreclr.dll')
)
foreach ($path in (@($mainExe) + $bridgeRequiredFiles)) {
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
    # Preserve every staged assembly, native library and runtime subdirectory.
    Copy-Item -LiteralPath $bridgeDirectory -Destination (Join-Path $packageDirectory 'bridge') -Recurse
    foreach ($inputFile in $bridgeRequiredFiles) {
        $relative = [IO.Path]::GetRelativePath($bridgeDirectory, $inputFile)
        $packagedFile = Join-Path (Join-Path $packageDirectory 'bridge') $relative
        if (-not (Test-Path -LiteralPath $packagedFile -PathType Leaf)) {
            throw "Required packaged bridge file is missing: $packagedFile"
        }
    }
    $sourceFileCount = @(Get-ChildItem -LiteralPath $bridgeDirectory -Recurse -File -Force).Count
    $packageFileCount = @(Get-ChildItem -LiteralPath (Join-Path $packageDirectory 'bridge') -Recurse -File -Force).Count
    if ($sourceFileCount -ne $packageFileCount) {
        throw "Bridge file count differs: source=$sourceFileCount package=$packageFileCount"
    }
    Write-Host "Verified self-contained bridge: $packageFileCount files"
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

<#
.SYNOPSIS
    Downloads the portable Node runtime that ships inside the installer.

.DESCRIPTION
    Fetches the official Windows x64 zip from nodejs.org, checks it against the SHASUMS256.txt of
    that same release, and unpacks it into build\node (node.exe at build\node\node.exe, npm at
    build\node\node_modules\npm). node.exe is never committed to the repository: both the CI build
    and a local build call this script.

.PARAMETER Version
    Node version to fetch, without the leading "v". Pinned to an LTS release on purpose.

.PARAMETER Destination
    Folder where node.exe ends up. Defaults to build\node next to the repository root.

.PARAMETER Force
    Downloads again even when the destination already holds that same version.
#>
[CmdletBinding()]
param(
    [string]$Version = '22.20.0',
    [string]$Destination,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path $repositoryRoot 'build\node' }

$nodeExecutable = Join-Path $Destination 'node.exe'
if ((Test-Path $nodeExecutable) -and (-not $Force)) {
    $installed = (& $nodeExecutable --version).Trim().TrimStart('v')
    if ($installed -eq $Version) {
        Write-Host "Node $Version ya esta en $Destination."
        exit 0
    }
    Write-Host "Hay Node $installed en $Destination y se pidio $Version; se reemplaza."
}

$archiveName = "node-v$Version-win-x64"
$baseUrl = "https://nodejs.org/dist/v$Version"
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("rudolph-node-" + [System.Guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    $zipPath = Join-Path $work "$archiveName.zip"
    Write-Host "Descargando $baseUrl/$archiveName.zip"
    Invoke-WebRequest -Uri "$baseUrl/$archiveName.zip" -OutFile $zipPath -UseBasicParsing

    Write-Host 'Verificando el hash publicado por nodejs.org'
    $sumsPath = Join-Path $work 'SHASUMS256.txt'
    Invoke-WebRequest -Uri "$baseUrl/SHASUMS256.txt" -OutFile $sumsPath -UseBasicParsing

    $expectedLine = Get-Content $sumsPath | Where-Object { $_ -match "\s$([regex]::Escape($archiveName))\.zip$" }
    if (-not $expectedLine) { throw "SHASUMS256.txt no menciona $archiveName.zip" }
    $expected = ($expectedLine -split '\s+')[0]
    $actual = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected.ToLowerInvariant()) {
        throw "El hash no coincide. Esperado $expected, obtenido $actual."
    }

    Write-Host "Descomprimiendo en $Destination"
    $extracted = Join-Path $work 'extracted'
    Expand-Archive -Path $zipPath -DestinationPath $extracted -Force

    if (Test-Path $Destination) { Remove-Item -Path $Destination -Recurse -Force }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Move-Item -Path (Join-Path $extracted $archiveName) -Destination $Destination

    $reported = (& $nodeExecutable --version).Trim()
    Write-Host "Listo: $nodeExecutable $reported"
}
finally {
    if (Test-Path $work) { Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue }
}

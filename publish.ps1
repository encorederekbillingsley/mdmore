<#
.SYNOPSIS
  Build a standalone, self-contained mdmore.exe and optionally add it to PATH.
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$OutDir  = "$PSScriptRoot\dist",
    # Where to install the exe so it is on your PATH.
    [string]$InstallDir = "$env:LOCALAPPDATA\mdmore",
    # Skip the install/PATH step and just build into -OutDir.
    [switch]$NoInstall
)

$ErrorActionPreference = 'Stop'

Write-Host "Publishing mdmore ($Runtime)..." -ForegroundColor Cyan
dotnet publish "$PSScriptRoot\mdmore.csproj" `
    -c Release -r $Runtime --self-contained `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:InvariantGlobalization=true `
    -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $OutDir 'mdmore.exe'
Write-Host "Built $exe" -ForegroundColor Green

if ($NoInstall) { return }

# Install: copy the exe to InstallDir and add that folder to the user PATH.
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item $exe -Destination $InstallDir -Force
Write-Host "Installed to $InstallDir" -ForegroundColor Green

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $InstallDir) {
    [Environment]::SetEnvironmentVariable('Path', "$userPath;$InstallDir", 'User')
    Write-Host "Added $InstallDir to your user PATH." -ForegroundColor Green
    Write-Host "Open a new terminal, then run:  mdmore <file.md>" -ForegroundColor Yellow
} else {
    Write-Host "Already on PATH. Run:  mdmore <file.md>" -ForegroundColor Yellow
}

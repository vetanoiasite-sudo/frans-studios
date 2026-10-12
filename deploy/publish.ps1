<#
.SYNOPSIS
  Builds the React app and the ASP.NET Core API into one IIS-ready folder.
  (Keep this file ASCII-only: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.)

.EXAMPLE
  .\deploy\publish.ps1                                  # -> .\publish
  .\deploy\publish.ps1 -Output D:\deploy\fransstudios
  .\deploy\publish.ps1 -SkipInstall                     # reuse node_modules (e.g. while the dev server runs)

  Then copy the folder to the server (or point the IIS site at it) and run, once per release:
    FransStudios.Api.exe migrate
#>
param(
  [string]$Output = (Join-Path $PSScriptRoot '..\publish'),
  [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Output = [System.IO.Path]::GetFullPath($Output)

Push-Location (Join-Path $root 'Frontend')
try {
  if (-not $SkipInstall) {
    Write-Host '1/4  Installing frontend dependencies (npm ci)...'
    npm ci --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed (stop the Vite dev server first, or use -SkipInstall)' }
  }
  else { Write-Host '1/4  Skipping npm ci (-SkipInstall)' }

  Write-Host '2/4  Building frontend...'
  npm run build
  if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
}
finally { Pop-Location }

Write-Host '3/4  Publishing API (Release, win-x64)...'
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
dotnet publish (Join-Path $root 'Backend\FransStudios.Api\FransStudios.Api.csproj') `
  -c Release -r win-x64 --self-contained false -o $Output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host '4/4  Copying frontend build into wwwroot...'
$wwwroot = Join-Path $Output 'wwwroot'
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
Copy-Item (Join-Path $root 'Frontend\dist') $wwwroot -Recurse

# Never ship development settings or local data.
Remove-Item (Join-Path $Output 'appsettings.Development.json') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $Output 'App_Data') -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Done -> $Output"
Write-Host 'Next: copy to the server, then run "FransStudios.Api.exe migrate" in that folder (see docs/DEPLOYMENT.md).'

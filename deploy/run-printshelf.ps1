# Runs the published PrintShelf build in Production mode.
# Used by the PrintShelf scheduled task (see install-task.ps1).
[CmdletBinding()]
param(
    [string]$PublishDir,
    [string]$Url = 'http://localhost:5100'
)

$ErrorActionPreference = 'Stop'
if (-not $PublishDir) { $PublishDir = Join-Path $PSScriptRoot '..\publish' }
$dll = Join-Path $PublishDir 'PrintShelf.Web.dll'
if (-not (Test-Path $dll)) {
    throw "Published app not found at $dll. Run: dotnet publish src/PrintShelf.Web -c Release -o publish -r win-x64 --self-contained false"
}

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = $Url

# Override the connection string on a deployed machine without editing files:
#   setx PRINTSHELF_CONN "Host=...;Port=5432;Database=printshelf;Username=printshelf;Password=..."
Set-Location $PublishDir
Write-Host "PrintShelf starting from $PublishDir on $Url"
dotnet $dll

# Backs up BOTH halves of PrintShelf (docs/06-deployment.md §5):
#   1. pg_dump custom-format database dump
#   2. data/ (library STLs + thumbnails) as a zip
# Requires pg_dump on PATH (PostgreSQL bin folder) and PGPASSWORD or .pgpass.
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$PgDump = 'pg_dump',
    [string]$DbHost = '127.0.0.1',
    [int]$Port = 5432,
    [string]$Database = 'printshelf',
    [string]$User = 'printshelf',
    [string]$DataDir,
    [int]$Keep = 14
)

$ErrorActionPreference = 'Stop'
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot '..\backups' }
if (-not $DataDir) { $DataDir = Join-Path $PSScriptRoot '..\publish\data' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dump = Join-Path $OutDir "printshelf-$stamp.dump"
$zip = Join-Path $OutDir "printshelf-$stamp-data.zip"

& $PgDump --host $DbHost --port $Port --username $User --dbname $Database `
    --format custom --file $dump
if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE" }

if (Test-Path $DataDir) {
    Compress-Archive -Path (Join-Path $DataDir '*') -DestinationPath $zip -CompressionLevel Optimal
} else {
    Write-Warning "Data folder not found: $DataDir (database dump still written)."
}

# Retention: keep the newest $Keep pairs.
Get-ChildItem $OutDir -Filter 'printshelf-*' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip ($Keep * 2) |
    Remove-Item -Force

Write-Host "Backup written to $OutDir"
Get-ChildItem $OutDir -Filter "printshelf-$stamp*" | Select-Object Name, Length

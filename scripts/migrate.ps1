[CmdletBinding()]
param(
    [string]$DbHost = "localhost",
    [int]$Port = 5432,
    [string]$Database = "projex_enterprise",
    [string]$MigrationsPath = (Join-Path $PSScriptRoot "..\database\migrations"),
    [string]$PsqlPath = "psql"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:PGPASSWORD)) {
    throw "Set PGPASSWORD to the projex_owner password for this process before running migrations."
}

$resolvedPsql = (Get-Command $PsqlPath -ErrorAction Stop).Source
$resolvedMigrations = (Resolve-Path $MigrationsPath -ErrorAction Stop).Path
$migrations = @(Get-ChildItem -LiteralPath $resolvedMigrations -Filter "*.sql" -File | Sort-Object Name)

if ($migrations.Count -eq 0) {
    throw "No SQL migration files were found in '$resolvedMigrations'."
}

$baseArgs = @(
    "-v", "ON_ERROR_STOP=1",
    "-h", $DbHost,
    "-p", "$Port",
    "-U", "projex_owner",
    "-d", $Database
)

& $resolvedPsql @baseArgs -c "CREATE TABLE IF NOT EXISTS public.schema_migrations (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());"
if ($LASTEXITCODE -ne 0) {
    throw "Could not initialize public.schema_migrations (psql exit code $LASTEXITCODE)."
}

foreach ($migration in $migrations) {
    $name = $migration.Name
    $sqlName = $name.Replace("'", "''")
    $checkSql = "SELECT 1 FROM public.schema_migrations WHERE name = '$sqlName';"
    $applied = & $resolvedPsql @baseArgs -tA -c $checkSql
    if ($LASTEXITCODE -ne 0) {
        throw "Could not check migration state for '$name' (psql exit code $LASTEXITCODE)."
    }

    if (($applied | Out-String).Trim() -eq "1") {
        Write-Host "skip  $name"
        continue
    }

    Write-Host "apply $name"
    $recordSql = "INSERT INTO public.schema_migrations(name) VALUES ('$sqlName');"
    & $resolvedPsql @baseArgs --single-transaction -f $migration.FullName -c $recordSql
    if ($LASTEXITCODE -ne 0) {
        throw "Migration '$name' failed. Its SQL and tracking record were run in one transaction; inspect the PostgreSQL error before retrying."
    }
}

Write-Host "All database migrations are applied."

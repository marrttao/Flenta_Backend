param(
    [Parameter(Position = 0)]
    [ValidateSet('add', 'update', 'list', 'script', 'check', 'bundle')]
    [string]$Action = 'list',
    [Parameter(Position = 1)]
    [string]$Name,
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'EF tool restore failed.' }
    $common = @('--project', 'src/Flenta.Infrastructure', '--startup-project', 'src/Flenta.Infrastructure', '--context', 'AppDbContext')
    $command = switch ($Action) {
        'add' {
            if ([string]::IsNullOrWhiteSpace($Name) -or $Name -notmatch '^[A-Za-z][A-Za-z0-9_]*$') {
                throw 'Provide a migration name, for example: ./scripts/ef.ps1 add AddSavingsGoals'
            }
            @('migrations', 'add', $Name, '--output-dir', 'Persistence/Migrations')
        }
        'update' { @('database', 'update') }
        'list' { @('migrations', 'list') }
        'check' { @('migrations', 'has-pending-model-changes') }
        'script' {
            if (!$Output) { $Output = 'artifacts/migrations.sql' }
            @('migrations', 'script', '--idempotent', '--output', $Output)
        }
        'bundle' {
            if (!$Output) { $Output = 'artifacts/efbundle' + $(if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }) }
            @('migrations', 'bundle', '--output', $Output)
        }
    }
    if ($Output) {
        $parent = Split-Path ([IO.Path]::GetFullPath($Output)) -Parent
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    & dotnet ef @command @common
    if ($LASTEXITCODE -ne 0) { throw "EF command '$Action' failed. See the output above." }
} finally { Pop-Location }

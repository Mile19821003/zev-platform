$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
Write-Output 'UAT finance seed: UAT-A01 / UAT-A02, UAT-OD1 / UAT-RF1 / UAT-CIS1.'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error 'Docker CLI is required.'
    exit 1
}

& docker compose up -d --build api
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    try {
        $response = Invoke-WebRequest 'http://localhost:8080/health/ready' -TimeoutSec 2 -UseBasicParsing
        if ($response.StatusCode -eq 200) { $ready = $true; break }
    } catch {
        Start-Sleep -Seconds 2
    }
}
if (-not $ready) {
    Write-Error 'API readiness did not return HTTP 200; UAT seed was not started.'
    exit 1
}

$dockerArguments = @('compose', 'exec', '-T', '-e', 'Seed__UatFinanceEnabled=true')
if (-not [string]::IsNullOrWhiteSpace($env:UAT_FINANCE_MANAGER_PASSWORD)) {
    if ([string]::IsNullOrWhiteSpace($env:UAT_FINANCE_MANAGER_EMAIL)) {
        $env:UAT_FINANCE_MANAGER_EMAIL = 'uat.finance.manager@example.test'
    }
    $dockerArguments += @('-e', 'UAT_FINANCE_MANAGER_EMAIL', '-e', 'UAT_FINANCE_MANAGER_PASSWORD')
}
$dockerArguments += @('api', 'dotnet', 'Zev.Api.dll', '--seed-uat-finance')

& docker @dockerArguments
exit $LASTEXITCODE

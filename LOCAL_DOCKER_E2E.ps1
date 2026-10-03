param(
    [string]$ApiBase = 'http://localhost:8080',
    [int]$HealthAttempts = 60
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$api = $ApiBase.TrimEnd('/')

function Assert-Status([string]$Name, [scriptblock]$Action) {
    try {
        & $Action
        Write-Output "PASS: $Name"
    } catch {
        Write-Error "FAIL: $Name ($($_.Exception.GetType().Name))"
        exit 1
    }
}

Assert-Status 'docker compose config' { docker compose config --quiet; if ($LASTEXITCODE -ne 0) { throw 'config' } }
Assert-Status 'docker compose build' { docker compose build; if ($LASTEXITCODE -ne 0) { throw 'build' } }
Assert-Status 'docker compose up -d' { docker compose up -d; if ($LASTEXITCODE -ne 0) { throw 'up' } }
Assert-Status 'docker compose ps' { docker compose ps; if ($LASTEXITCODE -ne 0) { throw 'ps' } }
& (Join-Path $PSScriptRoot 'scripts/health-smoke.ps1') -BaseUrl $api -Attempts $HealthAttempts
if ($LASTEXITCODE -ne 0) { exit 1 }

$email = $env:ZEV_E2E_EMAIL
$password = $env:ZEV_E2E_PASSWORD
if ([string]::IsNullOrWhiteSpace($email) -or [string]::IsNullOrWhiteSpace($password)) {
    Write-Output 'SKIPPED: login/API workflow; set ZEV_E2E_EMAIL and ZEV_E2E_PASSWORD in the process environment.'
    exit 0
}

try {
    $login = Invoke-RestMethod -Uri "$api/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ email = $email; password = $password } | ConvertTo-Json -Compress) -TimeoutSec 10
    if ([string]::IsNullOrWhiteSpace($login.accessToken)) { throw 'No access token' }
    $headers = @{ Authorization = "Bearer $($login.accessToken)" }
    $profile = Invoke-RestMethod -Uri "$api/api/me" -Headers $headers -TimeoutSec 10
    if (-not $profile.id) { throw 'Profile missing' }
    Write-Output 'PASS: login and /api/me'
    $accessible = Invoke-RestMethod -Uri "$api/api/me/zevs?pageSize=1" -Headers $headers -TimeoutSec 10
    if ($profile.isSystemAdministrator) {
        $null = Invoke-RestMethod -Uri "$api/api/system/organizations?pageSize=1" -Headers $headers -TimeoutSec 10
        $null = Invoke-RestMethod -Uri "$api/api/system/zevs?pageSize=1" -Headers $headers -TimeoutSec 10
        Write-Output 'PASS: administrator organization/ZEV lists'
    } else {
        Write-Output 'SKIPPED: administrator lists; supplied user is not System Administrator.'
    }
    if ($accessible.items.Count -gt 0) {
        $zevId = $accessible.items[0].id
        $null = Invoke-RestMethod -Uri "$api/api/zevs/$zevId" -Headers $headers -TimeoutSec 10
        if ($profile.isSystemAdministrator -or @($profile.memberships | Where-Object role -eq 'Manager').Count -gt 0) {
            $null = Invoke-RestMethod -Uri "$api/api/zevs/$zevId/registry/units?pageSize=1" -Headers $headers -TimeoutSec 10
            $null = Invoke-RestMethod -Uri "$api/api/zevs/$zevId/reports/summary" -Headers $headers -TimeoutSec 10
            Write-Output 'PASS: registry and report endpoints'
        } else { Write-Output 'SKIPPED: registry/report; supplied user lacks manager rights.' }
    } else { Write-Output 'SKIPPED: registry/report; no accessible ZEV.' }
    if ($env:ZEV_E2E_FORBIDDEN_ZEV_ID -and -not $profile.isSystemAdministrator) {
        try {
            $null = Invoke-WebRequest -Uri "$api/api/zevs/$($env:ZEV_E2E_FORBIDDEN_ZEV_ID)/registry/units" `
                -Headers $headers -TimeoutSec 10
            throw 'Forbidden ZEV was accessible'
        } catch {
            if ($_.Exception.Response.StatusCode -ne 404 -and $_.Exception.Response.StatusCode -ne 403) { throw }
            Write-Output 'PASS: cross-tenant registry denied'
        }
    } else { Write-Output 'SKIPPED: cross-tenant negative test; provide limited user and ZEV_E2E_FORBIDDEN_ZEV_ID.' }
} catch {
    Write-Error "FAIL: authenticated E2E workflow ($($_.Exception.GetType().Name))"
    exit 1
}

Write-Output 'PASS: executable local Docker E2E checks completed (see SKIPPED checks above).'
exit 0

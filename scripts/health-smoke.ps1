param(
    [string]$BaseUrl = 'http://localhost:8080',
    [int]$Attempts = 30,
    [int]$DelaySeconds = 2,
    [int]$TimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'
$base = $BaseUrl.TrimEnd('/')
for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    try {
        $live = Invoke-WebRequest -Uri "$base/health/live" -TimeoutSec $TimeoutSeconds
        $ready = Invoke-WebRequest -Uri "$base/health/ready" -TimeoutSec $TimeoutSeconds
        $readyBody = $ready.Content | ConvertFrom-Json
        if ($live.StatusCode -eq 200 -and $ready.StatusCode -eq 200 -and $readyBody.database -eq 'up') {
            Write-Output 'PASS: /health/live=200; /health/ready=200; database=up'
            exit 0
        }
    } catch {
        if ($attempt -eq $Attempts) {
            Write-Error "FAIL: health endpoints did not become ready after $Attempts attempts."
            exit 1
        }
    }
    Start-Sleep -Seconds $DelaySeconds
}
Write-Error 'FAIL: health endpoints returned unexpected status or body.'
exit 1

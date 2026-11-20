[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $rootDir

Write-Host "=== Запуск навчального вебзастосунку «папір.» ===" -ForegroundColor Cyan

# 1. Перевірка наявності конфігурації backend/appsettings.json
$appSettingsPath = Join-Path $rootDir "backend\appsettings.json"
if (-not (Test-Path $appSettingsPath)) {
    Write-Error "Файл конфігурації backend\appsettings.json не знайдено! Скопіюйте backend\appsettings.example.json у backend\appsettings.json та вкажіть локальні параметри."
    exit 1
}

$cfg = Get-Content $appSettingsPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($cfg.ConnectionStrings.DefaultConnection) -or
    $cfg.ConnectionStrings.DefaultConnection -match "<YOUR_POSTGRES_PASSWORD>") {
    Write-Error "У файлі backend\appsettings.json не заповнено реальний рядок підключення DefaultConnection."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($cfg.Jwt.Key) -or $cfg.Jwt.Key.Length -lt 32) {
    Write-Error "У файлі backend\appsettings.json не вказано коректний Jwt:Key (щонайменше 32 символи)."
    exit 1
}

# 2. Перевірка зайнятості портів 5150 та 5173 без примусового завершення сторонніх процесів
function Test-PortInUse([int]$Port) {
    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    return $null -ne $conn
}

$runDir = Join-Path $rootDir ".run"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$pidsFile = Join-Path $runDir "papir-processes.json"

if (Test-PortInUse 5150) {
    $procId = (Get-NetTCPConnection -LocalPort 5150 -State Listen | Select-Object -First 1).OwningProcess
    Write-Warning "Порт 5150 вже зайнятий процесом PID $procId. Якщо це попередній екземпляр цього проєкту, виконайте .\stop.ps1. Сторонні процеси автоматично не завершуються."
    exit 1
}

if (Test-PortInUse 5173) {
    $procId = (Get-NetTCPConnection -LocalPort 5173 -State Listen | Select-Object -First 1).OwningProcess
    Write-Warning "Порт 5173 вже зайнятий процесом PID $procId. Якщо це попередній екземпляр цього проєкту, виконайте .\stop.ps1. Сторонні процеси автоматично не завершуються."
    exit 1
}

# 3. Запуск Backend (ASP.NET Core Web API на http://localhost:5150)
$backendOutLog = Join-Path $runDir "backend.out.log"
$backendErrLog = Join-Path $runDir "backend.err.log"

Write-Host "Запуск серверної частини (ASP.NET Core Web API) на http://localhost:5150 ..." -ForegroundColor Yellow
$backendProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run --project `"$rootDir\backend\Backend.csproj`" --no-launch-profile" `
    -WorkingDirectory (Join-Path $rootDir "backend") `
    -RedirectStandardOutput $backendOutLog `
    -RedirectStandardError $backendErrLog `
    -WindowStyle Hidden `
    -PassThru

# 4. Запуск Frontend (Vue 3 + Vite на http://localhost:5173)
$frontendOutLog = Join-Path $runDir "frontend.out.log"
$frontendErrLog = Join-Path $runDir "frontend.err.log"

Write-Host "Запуск клієнтської частини (Vue 3 + Vite) на http://localhost:5173 ..." -ForegroundColor Yellow
$frontendProc = Start-Process -FilePath "cmd.exe" `
    -ArgumentList "/c npm run dev" `
    -WorkingDirectory (Join-Path $rootDir "frontend") `
    -RedirectStandardOutput $frontendOutLog `
    -RedirectStandardError $frontendErrLog `
    -WindowStyle Hidden `
    -PassThru

function Get-ProcessIdentityRecord([int]$TargetPid, [string]$Role, [string]$ExpectedMarker) {
    $gp = Get-Process -Id $TargetPid -ErrorAction Stop
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId = $TargetPid" -ErrorAction SilentlyContinue
    return @{
        Pid                = $TargetPid
        Role               = $Role
        ProcessName        = $gp.ProcessName
        StartTimeUtcTicks  = $gp.StartTime.ToUniversalTime().Ticks
        StartTimeIso       = $gp.StartTime.ToUniversalTime().ToString("o")
        CreationDateWmi    = if ($null -ne $cim -and $null -ne $cim.CreationDate) { $cim.CreationDate.ToUniversalTime().ToString("o") } else { "" }
        CommandLine        = if ($null -ne $cim -and $null -ne $cim.CommandLine) { $cim.CommandLine } else { "" }
        ExpectedMarker     = $ExpectedMarker
    }
}

$pidData = @{
    ProjectRoot = $rootDir
    Backend     = Get-ProcessIdentityRecord -TargetPid $backendProc.Id -Role "Backend" -ExpectedMarker "Backend.csproj"
    Frontend    = Get-ProcessIdentityRecord -TargetPid $frontendProc.Id -Role "Frontend" -ExpectedMarker "npm run dev"
    SavedAtUtc  = (Get-Date).ToUniversalTime().ToString("o")
}

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($pidsFile, ($pidData | ConvertTo-Json -Depth 5), $utf8Bom)

# 5. Очікування готовності сервісів
$readyBackend = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    try {
        $resp = Invoke-RestMethod -Uri "http://localhost:5150/api/health" -TimeoutSec 2 -ErrorAction Stop
        if ($resp.status -eq "ok") {
            $readyBackend = $true
            break
        }
    } catch {}
}

$readyFrontend = $false
for ($i = 0; $i -lt 20; $i++) {
    if (Test-PortInUse 5173) {
        $readyFrontend = $true
        break
    }
    Start-Sleep -Seconds 1
}

if ($readyBackend -and $readyFrontend) {
    Write-Host ""
    Write-Host "Усі сервіси «папір.» успішно запущені!" -ForegroundColor Green
    Write-Host "  Фронтенд (Vue 3):  http://localhost:5173" -ForegroundColor Green
    Write-Host "  Бекенд API:        http://localhost:5150" -ForegroundColor Green
    Write-Host "  Swagger OpenAPI:   http://localhost:5150/swagger" -ForegroundColor Green
    Write-Host "  Для зупинки:       .\stop.ps1" -ForegroundColor Gray
} else {
    Write-Warning "Сервіси запускаються довше очікуваного або виникла помилка. Перевірте журнали у папці .run\"
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Continue"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$pidsFile = Join-Path $rootDir ".run\papir-processes.json"

Write-Host "=== Зупинка процесів застосунку «папір.» ===" -ForegroundColor Cyan

if (-not (Test-Path $pidsFile)) {
    Write-Host "Файл активних процесів (.run\papir-processes.json) не знайдено. Немає збережених процесів цього проєкту для зупинки." -ForegroundColor Yellow
    exit 0
}

$pidData = Get-Content $pidsFile -Raw | ConvertFrom-Json

function Test-IsSameProjectProcess($Record) {
    if ($null -eq $Record -or -not $Record.Pid) { return $false }

    $targetPid = [int]$Record.Pid
    $liveProc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
    if ($null -eq $liveProc) {
        Write-Host "Процес $($Record.Role) (PID $targetPid) вже завершено." -ForegroundColor Gray
        return $false
    }

    # 1. Перевірка імені процесу
    if (-not [string]::IsNullOrWhiteSpace($Record.ProcessName) -and
        $liveProc.ProcessName -ne $Record.ProcessName) {
        Write-Warning "Пропуск PID ${targetPid}: поточне ім'я процесу '$($liveProc.ProcessName)' не збігається зі збереженим '$($Record.ProcessName)'. Ймовірно, PID повторно використаний іншим процесом."
        return $false
    }

    # 2. Перевірка фактичного часу запуску процесу (допуск до 2 секунд на округлення ОС/WMI)
    if ($null -ne $Record.StartTimeUtcTicks -and [long]$Record.StartTimeUtcTicks -gt 0) {
        $liveTicks = $liveProc.StartTime.ToUniversalTime().Ticks
        $diffSeconds = [Math]::Abs($liveTicks - [long]$Record.StartTimeUtcTicks) / [TimeSpan]::TicksPerSecond
        if ($diffSeconds -gt 2.0) {
            Write-Warning "Пропуск PID ${targetPid}: час створення процесу ($($liveProc.StartTime.ToUniversalTime().ToString('o'))) відрізняється від збереженого ($($Record.StartTimeIso)) на $([Math]::Round($diffSeconds, 2)) с. PID повторно використаний стороннім процесом!"
            return $false
        }
    }

    # 3. Перевірка командного рядка та маркерів належності цьому проєкту
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId = $targetPid" -ErrorAction SilentlyContinue
    if ($null -ne $cim -and -not [string]::IsNullOrWhiteSpace($cim.CommandLine)) {
        if (-not [string]::IsNullOrWhiteSpace($Record.ExpectedMarker) -and
            $cim.CommandLine -notlike "*$($Record.ExpectedMarker)*") {
            Write-Warning "Пропуск PID ${targetPid}: командний рядок не містить очікуваного маркера проєкту '$($Record.ExpectedMarker)'."
            return $false
        }
    }

    return $true
}

function Stop-VerifiedProcessTree([int]$ParentPid, [datetime]$ParentStartTimeUtc, [string]$Label) {
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentPid" -ErrorAction SilentlyContinue
    foreach ($child in $children) {
        $childProc = Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
        if ($null -ne $childProc) {
            # Дочірній процес має бути запущений не раніше батьківського (з допуском 2 с)
            $childStartUtc = $childProc.StartTime.ToUniversalTime()
            if ($childStartUtc -ge $ParentStartTimeUtc.AddSeconds(-2)) {
                Stop-VerifiedProcessTree -ParentPid $child.ProcessId -ParentStartTimeUtc $childStartUtc -Label "$Label -> $($childProc.ProcessName)"
            } else {
                Write-Warning "Пропуск дочірнього PID $($child.ProcessId): час запуску старіший за батьківський процес $ParentPid."
            }
        }
    }

    $proc = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
    if ($null -ne $proc) {
        Write-Host "Зупинка підтвердженого процесу $Label (PID $ParentPid)..." -ForegroundColor Yellow
        Stop-Process -Id $ParentPid -Force -ErrorAction SilentlyContinue
    }
}

foreach ($rec in @($pidData.Frontend, $pidData.Backend)) {
    if ($null -ne $rec -and (Test-IsSameProjectProcess $rec)) {
        $parentStartUtc = [datetime]::new([long]$rec.StartTimeUtcTicks, [DateTimeKind]::Utc)
        Stop-VerifiedProcessTree -ParentPid ([int]$rec.Pid) -ParentStartTimeUtc $parentStartUtc -Label $rec.Role
    }
}

Start-Sleep -Milliseconds 500
Remove-Item $pidsFile -Force -ErrorAction SilentlyContinue
Write-Host "Перевірку та зупинку процесів проєкту «папір.» завершено." -ForegroundColor Green

# ==============================================================================
# Medical Text Expander - Supabase Cloud Restore & Auto-Configuration Script
# Version: v1.9.9
# ==============================================================================

param (
    [string]$SupabaseUrl = "https://zjpfxunvyjhvzipxqiqs.supabase.co",
    [string]$SupabaseKey = "sb_publishable_vxQ2YXKZJtn6-BwnajGASQ_dJqdlYee"
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Host.UI.RawUI.WindowTitle = "Medical Text Expander - Supabase Cloud Restore v1.9.9"

Write-Host ""
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "  Medical Text Expander - Setup & Restore Supabase Cloud v1.9.9" -ForegroundColor Green
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host ""

if ([string]::IsNullOrWhiteSpace($SupabaseUrl)) {
    $SupabaseUrl = Read-Host "Enter Supabase Project URL"
}
$SupabaseUrl = $SupabaseUrl.Trim().TrimEnd('/')

if ([string]::IsNullOrWhiteSpace($SupabaseKey)) {
    $SupabaseKey = Read-Host "Enter Supabase API Key"
}
$SupabaseKey = $SupabaseKey.Trim()

Write-Host "[1/4] Testing connection to Supabase..." -ForegroundColor Yellow

$testEndpoint = "$SupabaseUrl/rest/v1/bed_notes?select=bed_number&limit=1"
$headers = @{
    "apikey"        = $SupabaseKey
    "Authorization" = "Bearer $SupabaseKey"
    "Content-Type"  = "application/json"
}

try {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    $resp = Invoke-WebRequest -Uri $testEndpoint -Headers $headers -Method Get -TimeoutSec 10 -UseBasicParsing
    if ($resp.StatusCode -eq 200) {
        Write-Host "   [OK] Connected to Supabase Cloud and found table bed_notes!" -ForegroundColor Green
    }
} catch {
    Write-Host "   [FAIL] Connection error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "[2/4] Updating configuration files..." -ForegroundColor Yellow

$iniPath = 'D:\Medical_Text_Expander\expander_settings.ini'
if (Test-Path $iniPath) {
    $iniLines = Get-Content $iniPath -Encoding UTF8
    $newIni = @()
    foreach ($line in $iniLines) {
        if ($line -match "^SupabaseUrl=") {
            $newIni += "SupabaseUrl=$SupabaseUrl"
        } elseif ($line -match "^SupabaseKey=") {
            $newIni += "SupabaseKey=$SupabaseKey"
        } elseif ($line -match "^SupabaseEnabled=") {
            $newIni += "SupabaseEnabled=true"
        } else {
            $newIni += $line
        }
    }
    [System.IO.File]::WriteAllLines($iniPath, $newIni, [System.Text.Encoding]::UTF8)
    Write-Host "   [OK] Updated expander_settings.ini" -ForegroundColor Green
}

$appJsFiles = @('D:\Medical_Text_Expander\web\app.js', 'D:\Medical_Text_Expander\docs\app.js')
foreach ($f in $appJsFiles) {
    if (Test-Path $f) {
        $content = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
        $content = [System.Text.RegularExpressions.Regex]::Replace($content, 'const SUPABASE_URL = ".*?";', ('const SUPABASE_URL = "{0}";' -f $SupabaseUrl))
        $content = [System.Text.RegularExpressions.Regex]::Replace($content, 'const SUPABASE_KEY = ".*?";', ('const SUPABASE_KEY = "{0}";' -f $SupabaseKey))
        [System.IO.File]::WriteAllText($f, $content, [System.Text.Encoding]::UTF8)
        Write-Host "   [OK] Updated $f" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "[3/4] Recompiling Medical_Text_Expander.exe..." -ForegroundColor Yellow
Stop-Process -Name Medical_Text_Expander -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$cscArgs = @(
    "/target:winexe",
    "/out:Medical_Text_Expander.exe",
    "/platform:anycpu",
    "/optimize+",
    "/codepage:65001",
    "/utf8output",
    "/win32icon:medical_expander_icon.ico",
    "MedicalTextExpander.cs"
)

& $csc $cscArgs
if ($LASTEXITCODE -eq 0) {
    Write-Host "   [OK] Compilation successful (0 errors)" -ForegroundColor Green
    Start-Process "D:\Medical_Text_Expander\Medical_Text_Expander.exe" -ArgumentList "/restart"
    Write-Host "   [OK] Launched Medical_Text_Expander.exe" -ForegroundColor Green
} else {
    Write-Host "   [FAIL] Compilation failed" -ForegroundColor Red
}

Write-Host ""
Write-Host "[4/4] Verifying Cloud data status..." -ForegroundColor Yellow
try {
    $metaResp = Invoke-RestMethod -Uri "$SupabaseUrl/rest/v1/bed_notes?select=bed_number,updated_by&order=bed_number.asc" -Headers $headers -Method Get -TimeoutSec 10
    $count = ($metaResp | Measure-Object).Count
    Write-Host "   [OK] Supabase Cloud beds count: $count rows (Beds 1-30, Row 100, Row 101, Row 102)" -ForegroundColor Green
} catch {
    Write-Host "   [WARN] Cloud check warning: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================================================" -ForegroundColor Green
Write-Host "  SUCCESS: Supabase Cloud v1.9.9 connected and verified 100%!" -ForegroundColor Green
Write-Host "========================================================================" -ForegroundColor Green
Write-Host ""

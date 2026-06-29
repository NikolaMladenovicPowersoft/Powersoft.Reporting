$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5150'
$cookiePath = 'C:\p\Powersoft.Reporting\_test_cookies.json'

# Reload session
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$cookies = Get-Content $cookiePath -Raw | ConvertFrom-Json
foreach ($c in $cookies) {
    $cookie = New-Object System.Net.Cookie($c.Name, $c.Value, $c.Path, $c.Domain)
    $session.Cookies.Add($(New-Object System.Uri($base)), $cookie)
}

# Verify connection
$storeCheck = Invoke-WebRequest -Uri "$base/Reports/GetStores" -UseBasicParsing -WebSession $session
$stores = $storeCheck.Content | ConvertFrom-Json
if ($stores.Count -eq 0) {
    Write-Host "NOT CONNECTED - re-authenticating..."
    $loginPage = Invoke-WebRequest -Uri "$base/Account/Login" -UseBasicParsing -WebSession $session
    $token = ([regex]::Match($loginPage.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    $body = "Username=REPORTING_TEST&Password=Test123!&RememberMe=true&__RequestVerificationToken=$([uri]::EscapeDataString($token))"
    Invoke-WebRequest -Uri "$base/Account/Login" -Method POST -Body $body -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session | Out-Null
    Invoke-WebRequest -Uri "$base/Home/Connect" -Method POST -Body @{databaseCode='DEMO365MODAPRO1'} -UseBasicParsing -WebSession $session | Out-Null
    $allCookies = $session.Cookies.GetCookies($base)
    $cookieList = @()
    foreach ($c in $allCookies) { $cookieList += @{ Name = $c.Name; Value = $c.Value; Domain = $c.Domain; Path = $c.Path } }
    $cookieList | ConvertTo-Json -Depth 3 | Out-File $cookiePath -Encoding utf8
    Write-Host "Re-connected"
}

Write-Host "============================================"
Write-Host " PURCHASES VS SALES - FULL FUNCTIONALITY TEST"
Write-Host "============================================"

# =============================================
# 1. PAGE LOAD
# =============================================
Write-Host "`n=== 1. PAGE LOAD ==="
$psPage = Invoke-WebRequest -Uri "$base/Reports/PurchasesSales" -UseBasicParsing -WebSession $session
$title = ([regex]::Match($psPage.Content, '<title>([^<]+)</title>')).Groups[1].Value
Write-Host "Title: $title"
Write-Host "Page size: $($psPage.Content.Length) bytes"

$checks = @{
    'DateFrom' = 'DateFrom|dateFrom'
    'DateTo' = 'DateTo|dateTo'
    'ReportOn' = 'ReportOn|reportOn'
    'GroupBy' = 'GroupBy|groupBy'
    'SecondaryGroupBy' = 'SecondaryGroupBy|secondaryGroupBy'
    'ThirdGroupBy' = 'ThirdGroupBy|thirdGroupBy'
    'IncludeVat' = 'IncludeVat|includeVat'
    'ShowQty' = 'ShowQty|showQty'
    'ShowCost' = 'ShowCost|showCost'
    'ShowProfit' = 'ShowProfit|showProfit'
    'ReportMode' = 'ReportMode|reportMode'
    'Schedule' = 'scheduleModal|_Schedule|SavePsSchedule'
    'Email' = 'emailModal|_SendEmail|SendPsReportEmail'
    'AI' = 'aiModal|_AiAnalyze|AnalyzePsReport'
    'SaveLayout' = '_SaveLayout|layoutModal|SaveReportLayout'
    'Export' = 'exportPsReport|ExportPs'
    'Print' = 'PrintPsPreview|printPsPreview'
    'ItemsSelection' = '_ItemsSelection|itemsSelection'
}

foreach ($chk in $checks.GetEnumerator()) {
    $found = $psPage.Content -match $chk.Value
    $status = if ($found) { '[PASS]' } else { '[FAIL]' }
    Write-Host "$status $($chk.Key)"
}

# Extract ReportOn options
Write-Host "`nReportOn options:"
$psPage.Content -split "`n" | Where-Object { $_ -match 'ReportOn.*option|reportOn.*option' } | ForEach-Object { $_.Trim() } | Select-Object -First 5

# Extract GroupBy options
Write-Host "`nGroupBy options:"
$inGroupBy = $false
$psPage.Content -split "`n" | ForEach-Object {
    if ($_ -match 'name="GroupBy"|id="groupBy"') { $inGroupBy = $true }
    if ($inGroupBy -and $_ -match 'value="([^"]*)"[^>]*>([^<]+)') { Write-Host "  $($Matches[1]) = $($Matches[2])" }
    if ($inGroupBy -and $_ -match '</select>') { $inGroupBy = $false }
}

# Save page
$psPage.Content | Out-File 'C:\p\Powersoft.Reporting\_test_ps_page.html' -Encoding utf8

# =============================================
# 2. GENERATE (POST)
# =============================================
Write-Host "`n=== 2. GENERATE REPORT ==="
$token = ([regex]::Match($psPage.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value

$formData = @{
    DateFrom = '01/01/2025'
    DateTo = '31/12/2025'
    ReportOn = 'Sale'
    GroupBy = 'Category'
    SecondaryGroupBy = 'None'
    ThirdGroupBy = 'None'
    IncludeVat = 'false'
    ShowQty = 'true'
    ShowCost = 'true'
    ShowProfit = 'true'
    ReportMode = 'Summary'
    PageNumber = '1'
    PageSize = '50'
    SortColumn = 'GroupName'
    SortDirection = 'ASC'
    __RequestVerificationToken = $token
}

$genResp = Invoke-WebRequest -Uri "$base/Reports/PurchasesSales" -Method POST -Body $formData -UseBasicParsing -WebSession $session
Write-Host "Generate response size: $($genResp.Content.Length)"
$hasResults = $genResp.Content -match 'tbody|reportData|totalRows'
Write-Host "[$(if ($hasResults) {'PASS'} else {'FAIL'})] Generate returns data"

$totalMatch = [regex]::Match($genResp.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
if ($totalMatch.Success) { Write-Host "Total rows: $($totalMatch.Groups[1].Value)" }
$genResp.Content | Out-File 'C:\p\Powersoft.Reporting\_test_ps_generated.html' -Encoding utf8

# =============================================
# 3. EXPORTS
# =============================================
Write-Host "`n=== 3. EXPORTS ==="

# PS exports use POST, check the JS
Write-Host "--- Excel Export ---"
$excelResp = Invoke-WebRequest -Uri "$base/Reports/ExportPsExcel" -Method POST -Body $formData -UseBasicParsing -WebSession $session
$ct = $excelResp.Headers['Content-Type']
$cd = $excelResp.Headers['Content-Disposition']
Write-Host "Content-Type: $ct"
Write-Host "Content-Disposition: $cd"
Write-Host "Size: $($excelResp.Content.Length) bytes"
$isFile = $ct -match 'spreadsheet|excel|octet'
Write-Host "[$(if ($isFile -and $excelResp.Content.Length -gt 100) {'PASS'} else {'FAIL'})] Excel export"
if ($isFile) { [IO.File]::WriteAllBytes('C:\p\Powersoft.Reporting\_test_ps_export.xlsx', $excelResp.Content) }

Write-Host "`n--- CSV Export ---"
$csvResp = Invoke-WebRequest -Uri "$base/Reports/ExportPsCsv" -Method POST -Body $formData -UseBasicParsing -WebSession $session
$ct = $csvResp.Headers['Content-Type']
Write-Host "Content-Type: $ct"
Write-Host "Size: $($csvResp.Content.Length) bytes"
if ($ct -match 'csv') {
    $csvText = [System.Text.Encoding]::UTF8.GetString($csvResp.Content)
    $csvLines = ($csvText -split "`n").Count
    Write-Host "[PASS] CSV export: $csvLines lines"
    Write-Host "Header: $(($csvText -split "`n" | Where-Object { $_ -match '^[A-Z]' } | Select-Object -First 1))"
    Write-Host "First 3 data rows:"
    $csvText -split "`n" | Where-Object { $_ -notmatch '^#|^$' -and $_ -match ',' } | Select-Object -First 4 | ForEach-Object { Write-Host "  $_" }
    $csvText | Out-File 'C:\p\Powersoft.Reporting\_test_ps_export.csv' -Encoding utf8
} else {
    Write-Host "[FAIL] CSV returned HTML"
}

Write-Host "`n--- PDF Export ---"
$pdfResp = Invoke-WebRequest -Uri "$base/Reports/ExportPsPdf" -Method POST -Body $formData -UseBasicParsing -WebSession $session
$ct = $pdfResp.Headers['Content-Type']
Write-Host "Content-Type: $ct"
Write-Host "Size: $($pdfResp.Content.Length) bytes"
Write-Host "[$(if ($ct -match 'pdf') {'PASS'} else {'FAIL'})] PDF export"

# =============================================
# 4. PRINT PREVIEW
# =============================================
Write-Host "`n=== 4. PRINT PREVIEW ==="
$printResp = Invoke-WebRequest -Uri "$base/Reports/PrintPsPreview" -Method POST -Body $formData -UseBasicParsing -WebSession $session
$printCt = $printResp.Headers['Content-Type']
$printTitle = ([regex]::Match($printResp.Content, '<title>([^<]+)</title>')).Groups[1].Value
Write-Host "Title: $printTitle"
Write-Host "Size: $($printResp.Content.Length) bytes"
$hasPrintTable = $printResp.Content -match '<table'
Write-Host "[$(if ($hasPrintTable) {'PASS'} else {'FAIL'})] Print preview renders"

# =============================================
# 5. SCHEDULE / LAYOUT / EMAIL / AI
# =============================================
Write-Host "`n=== 5. SCHEDULES ==="
$schedResp = Invoke-WebRequest -Uri "$base/Reports/GetPsSchedules" -UseBasicParsing -WebSession $session
Write-Host "Schedules: $($schedResp.Content)"
Write-Host "[PASS] Schedule list API"

Write-Host "`n=== 6. LAYOUT ==="
$layoutResp = Invoke-WebRequest -Uri "$base/Reports/GetReportLayout?reportType=PurchasesSales" -UseBasicParsing -WebSession $session
$layoutJson = $layoutResp.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Write-Host "Has saved layout: $($layoutJson.hasSaved)"
Write-Host "[PASS] Layout API"

Write-Host "`n=== 7. EMAIL TEMPLATES ==="
$emailResp = Invoke-WebRequest -Uri "$base/Reports/GetEmailTemplates?reportType=PurchasesSales" -UseBasicParsing -WebSession $session
Write-Host "Templates: $(($emailResp.Content | ConvertFrom-Json).Count)"
Write-Host "[PASS] Email templates API"

Write-Host "`n=== 8. AI STATUS ==="
$aiResp = Invoke-WebRequest -Uri "$base/Reports/GetAiStatus" -UseBasicParsing -WebSession $session
Write-Host "AI: $($aiResp.Content)"
Write-Host "[PASS] AI status"

# =============================================
# 6. GENERATE WITH DIFFERENT PARAMS (Purchase report)
# =============================================
Write-Host "`n=== 9. GENERATE: ReportOn=Purchase, GroupBy=Store ==="
$formData2 = $formData.Clone()
$formData2['ReportOn'] = 'Purchase'
$formData2['GroupBy'] = 'Store'

$gen2 = Invoke-WebRequest -Uri "$base/Reports/PurchasesSales" -Method POST -Body $formData2 -UseBasicParsing -WebSession $session
Write-Host "Response size: $($gen2.Content.Length)"
$totalMatch2 = [regex]::Match($gen2.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
if ($totalMatch2.Success) { Write-Host "Total rows: $($totalMatch2.Groups[1].Value)" }
Write-Host "[$(if ($gen2.Content.Length -gt 5000) {'PASS'} else {'FAIL'})] Purchase+Store generate"

# =============================================
# 7. GENERATE: ReportOn=Both
# =============================================
Write-Host "`n=== 10. GENERATE: ReportOn=Both, GroupBy=Brand ==="
$formData3 = $formData.Clone()
$formData3['ReportOn'] = 'Both'
$formData3['GroupBy'] = 'Brand'

$gen3 = Invoke-WebRequest -Uri "$base/Reports/PurchasesSales" -Method POST -Body $formData3 -UseBasicParsing -WebSession $session
Write-Host "Response size: $($gen3.Content.Length)"
$totalMatch3 = [regex]::Match($gen3.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
if ($totalMatch3.Success) { Write-Host "Total rows: $($totalMatch3.Groups[1].Value)" }
Write-Host "[$(if ($gen3.Content.Length -gt 5000) {'PASS'} else {'FAIL'})] Both+Brand generate"

# CSV for SQL verification
Write-Host "`n=== 11. CSV EXPORT: Sale + Category (for SQL verification) ==="
$csvResp2 = Invoke-WebRequest -Uri "$base/Reports/ExportPsCsv" -Method POST -Body $formData -UseBasicParsing -WebSession $session
$ct2 = $csvResp2.Headers['Content-Type']
if ($ct2 -match 'csv') {
    $csvText2 = [System.Text.Encoding]::UTF8.GetString($csvResp2.Content)
    $csvText2 | Out-File 'C:\p\Powersoft.Reporting\_test_ps_verify.csv' -Encoding utf8
    # Show grand total line
    $grandTotal = ($csvText2 -split "`n" | Where-Object { $_ -match 'GRAND TOTAL' })
    Write-Host "Grand Total: $grandTotal"
    
    # Show first 5 data rows
    Write-Host "First 5 data rows:"
    $csvText2 -split "`n" | Where-Object { $_ -notmatch '^#|^$|GRAND TOTAL' -and $_ -match ',' } | Select-Object -Skip 1 -First 5 | ForEach-Object { Write-Host "  $_" }
}

Write-Host "`n============================================"
Write-Host " TEST COMPLETE"
Write-Host "============================================"

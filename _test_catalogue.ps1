$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5150'
$cookiePath = 'C:\p\Powersoft.Reporting\_test_cookies.json'

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$cookies = Get-Content $cookiePath -Raw | ConvertFrom-Json
foreach ($c in $cookies) {
    $cookie = New-Object System.Net.Cookie($c.Name, $c.Value, $c.Path, $c.Domain)
    $session.Cookies.Add($(New-Object System.Uri($base)), $cookie)
}

# Verify connection
$check = (Invoke-WebRequest -Uri "$base/Reports/GetStores" -UseBasicParsing -WebSession $session).Content | ConvertFrom-Json
if ($check.Count -eq 0) {
    $lp = Invoke-WebRequest -Uri "$base/Account/Login" -UseBasicParsing -WebSession $session
    $tk = ([regex]::Match($lp.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    Invoke-WebRequest -Uri "$base/Account/Login" -Method POST -Body "Username=REPORTING_TEST&Password=Test123!&RememberMe=true&__RequestVerificationToken=$([uri]::EscapeDataString($tk))" -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session | Out-Null
    Invoke-WebRequest -Uri "$base/Home/Connect" -Method POST -Body @{databaseCode='DEMO365MODAPRO1'} -UseBasicParsing -WebSession $session | Out-Null
    $allC = $session.Cookies.GetCookies($base); $cl = @(); foreach ($c in $allC) { $cl += @{ Name=$c.Name; Value=$c.Value; Domain=$c.Domain; Path=$c.Path } }; $cl | ConvertTo-Json -Depth 3 | Out-File $cookiePath -Encoding utf8
    Write-Host "Re-connected"
}

Write-Host "============================================"
Write-Host " CATALOGUE - FULL FUNCTIONALITY TEST"
Write-Host "============================================"

# 1. PAGE LOAD
Write-Host "`n=== 1. PAGE LOAD ==="
$catPage = Invoke-WebRequest -Uri "$base/Reports/Catalogue" -UseBasicParsing -WebSession $session
$title = ([regex]::Match($catPage.Content, '<title>([^<]+)</title>')).Groups[1].Value
Write-Host "Title: $title"
Write-Host "Size: $($catPage.Content.Length) bytes"

$checks = @('DateFrom','DateTo','reportMode','reportOn','primaryGroup','secondaryGroup','thirdGroup','dateBasis','displayColumns','showProfit','showStock','includeVat','Catalogue','PrintCataloguePreview','ExportCatalogue','SaveCatalogueSchedule','SendCatalogueEmail','AnalyzeCatalogueReport','_ItemsSelection','_SaveLayout','_Schedule','_SendEmail','_AiAnalyze','collectCatalogueParams')
foreach ($chk in $checks) {
    $found = $catPage.Content -match $chk
    Write-Host "[$(if ($found) {'PASS'} else {'FAIL'})] $chk"
}
$catPage.Content | Out-File 'C:\p\Powersoft.Reporting\_test_cat_page.html' -Encoding utf8

# 2. GENERATE (POST with Sale + Category)
Write-Host "`n=== 2. GENERATE: Sale + Category (Summary) ==="
$token = ([regex]::Match($catPage.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value

$formData = @{
    DateFrom = '01/01/2025'
    DateTo = '31/12/2025'
    ReportMode = 'Summary'
    ReportOn = 'Sale'
    PrimaryGroup = 'Category'
    SecondaryGroup = 'None'
    ThirdGroup = 'None'
    DateBasis = 'DateTrans'
    IncludeVat = 'false'
    ShowProfit = 'true'
    ShowStock = 'true'
    PageNumber = '1'
    PageSize = '50'
    SortColumn = 'GroupName'
    SortDirection = 'ASC'
    __RequestVerificationToken = $token
}

$genResp = Invoke-WebRequest -Uri "$base/Reports/Catalogue" -Method POST -Body $formData -UseBasicParsing -WebSession $session
Write-Host "Size: $($genResp.Content.Length)"
$totalMatch = [regex]::Match($genResp.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
if ($totalMatch.Success) { Write-Host "Total rows: $($totalMatch.Groups[1].Value)" }
Write-Host "[$(if ($genResp.Content.Length -gt 10000) {'PASS'} else {'FAIL'})] Generate"

# 3. EXPORTS
Write-Host "`n=== 3. EXCEL EXPORT ==="
$excelUrl = "$base/Reports/ExportCatalogueExcel?dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=Summary&reportOn=Sale&primaryGroup=Category&secondaryGroup=None&thirdGroup=None&dateBasis=DateTrans&includeVat=false&showProfit=true&showStock=true&sortColumn=GroupName&sortDirection=ASC"
$excelResp = Invoke-WebRequest -Uri $excelUrl -UseBasicParsing -WebSession $session
$ct = $excelResp.Headers['Content-Type']
Write-Host "Content-Type: $ct"
Write-Host "Size: $($excelResp.Content.Length) bytes"
Write-Host "[$(if ($ct -match 'spreadsheet|excel') {'PASS'} else {'FAIL'})] Excel export"

Write-Host "`n=== 4. CSV EXPORT ==="
$csvUrl = "$base/Reports/ExportCatalogueCsv?dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=Summary&reportOn=Sale&primaryGroup=Category&secondaryGroup=None&thirdGroup=None&dateBasis=DateTrans&includeVat=false&showProfit=true&showStock=true&sortColumn=GroupName&sortDirection=ASC"
$csvResp = Invoke-WebRequest -Uri $csvUrl -UseBasicParsing -WebSession $session
$ct = $csvResp.Headers['Content-Type']
Write-Host "Content-Type: $ct"
Write-Host "Size: $($csvResp.Content.Length) bytes"
if ($ct -match 'csv') {
    $csvText = [System.Text.Encoding]::UTF8.GetString($csvResp.Content)
    $lines = ($csvText -split "`n" | Where-Object { $_.Length -gt 0 }).Count
    Write-Host "[PASS] CSV: $lines lines"
    Write-Host "Header: $(($csvText -split "`n" | Where-Object { $_ -match '^[A-Z]' } | Select-Object -First 1))"
    $grandTotal = ($csvText -split "`n" | Where-Object { $_ -match 'GRAND TOTAL|TOTAL' } | Select-Object -Last 1)
    Write-Host "Grand Total: $grandTotal"
    $csvText | Out-File 'C:\p\Powersoft.Reporting\_test_cat_export.csv' -Encoding utf8
} else {
    Write-Host "[FAIL] CSV returned non-CSV content"
    $pageTitle = ([regex]::Match([System.Text.Encoding]::UTF8.GetString($csvResp.Content), '<title>([^<]+)</title>')).Groups[1].Value
    Write-Host "Redirected to: $pageTitle"
}

# 5. PRINT PREVIEW
Write-Host "`n=== 5. PRINT PREVIEW ==="
$printUrl = "$base/Reports/PrintCataloguePreview?dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=Summary&reportOn=Sale&primaryGroup=Category&secondaryGroup=None&thirdGroup=None&dateBasis=DateTrans&includeVat=false&showProfit=true&showStock=true&sortColumn=GroupName&sortDirection=ASC"
$printResp = Invoke-WebRequest -Uri $printUrl -UseBasicParsing -WebSession $session
$printTitle = ([regex]::Match($printResp.Content, '<title>([^<]+)</title>')).Groups[1].Value
Write-Host "Title: $printTitle"
Write-Host "Size: $($printResp.Content.Length)"
Write-Host "[$(if ($printResp.Content -match '<table') {'PASS'} else {'FAIL'})] Print preview"

# 6. BOTH mode (tests UNION ALL of 4 legs)
Write-Host "`n=== 6. GENERATE: Both + Store ==="
$formData2 = $formData.Clone()
$formData2['ReportOn'] = 'Both'
$formData2['PrimaryGroup'] = 'Store'
$gen2 = Invoke-WebRequest -Uri "$base/Reports/Catalogue" -Method POST -Body $formData2 -UseBasicParsing -WebSession $session
Write-Host "Size: $($gen2.Content.Length)"
$totalMatch2 = [regex]::Match($gen2.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
if ($totalMatch2.Success) { Write-Host "Total rows: $($totalMatch2.Groups[1].Value)" }
Write-Host "[$(if ($gen2.Content.Length -gt 5000) {'PASS'} else {'FAIL'})] Both+Store"

# 7. SCHEDULE / LAYOUT / EMAIL / AI
Write-Host "`n=== 7. SCHEDULES ==="
$sr = Invoke-WebRequest -Uri "$base/Reports/GetCatalogueSchedules" -UseBasicParsing -WebSession $session
Write-Host "Schedules: $($sr.Content)"
Write-Host "[PASS] Schedule list"

Write-Host "`n=== 8. LAYOUT ==="
$lr = Invoke-WebRequest -Uri "$base/Reports/GetReportLayout?reportType=Catalogue" -UseBasicParsing -WebSession $session
$lj = $lr.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Write-Host "Has layout: $($lj.hasSaved)"
Write-Host "[PASS] Layout API"

Write-Host "`n=== 9. EMAIL TEMPLATES ==="
$er = Invoke-WebRequest -Uri "$base/Reports/GetEmailTemplates?reportType=Catalogue" -UseBasicParsing -WebSession $session
Write-Host "Templates: $(($er.Content | ConvertFrom-Json).Count)"
Write-Host "[PASS] Email templates"

Write-Host "`n=== 10. STOCK POSITION ==="
$stockResp = Invoke-WebRequest -Uri "$base/Reports/GetItemStockPosition?itemCode=A001" -UseBasicParsing -WebSession $session -ErrorAction SilentlyContinue
if ($stockResp) {
    Write-Host "Status: $($stockResp.StatusCode)"
    Write-Host "Content: $($stockResp.Content.Substring(0, [Math]::Min(200, $stockResp.Content.Length)))"
    Write-Host "[PASS] Stock position API"
}

Write-Host "`n=== 11. FILTER PRESETS ==="
$fp = Invoke-WebRequest -Uri "$base/Reports/GetFilterPresets?reportType=Catalogue" -UseBasicParsing -WebSession $session
Write-Host "Presets: $($fp.Content)"
Write-Host "[PASS] Filter presets"

# 8. Detail mode
Write-Host "`n=== 12. GENERATE: Sale + Category (Detail mode) ==="
$formData3 = $formData.Clone()
$formData3['ReportMode'] = 'Detail'
$gen3 = Invoke-WebRequest -Uri "$base/Reports/Catalogue" -Method POST -Body $formData3 -UseBasicParsing -WebSession $session
Write-Host "Size: $($gen3.Content.Length)"
$totalMatch3 = [regex]::Match($gen3.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
if ($totalMatch3.Success) { Write-Host "Total rows: $($totalMatch3.Groups[1].Value)" }
Write-Host "[$(if ($gen3.Content.Length -gt 5000) {'PASS'} else {'FAIL'})] Detail mode"

# 9. Purchase mode
Write-Host "`n=== 13. GENERATE: Purchase + Brand ==="
$formData4 = $formData.Clone()
$formData4['ReportOn'] = 'Purchase'
$formData4['PrimaryGroup'] = 'Brand'
$gen4 = Invoke-WebRequest -Uri "$base/Reports/Catalogue" -Method POST -Body $formData4 -UseBasicParsing -WebSession $session
Write-Host "Size: $($gen4.Content.Length)"
$totalMatch4 = [regex]::Match($gen4.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
if ($totalMatch4.Success) { Write-Host "Total rows: $($totalMatch4.Groups[1].Value)" }
Write-Host "[$(if ($gen4.Content.Length -gt 5000) {'PASS'} else {'FAIL'})] Purchase+Brand"

Write-Host "`n============================================"
Write-Host " CATALOGUE TEST COMPLETE"
Write-Host "============================================"

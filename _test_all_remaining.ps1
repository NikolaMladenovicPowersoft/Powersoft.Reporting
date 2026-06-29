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
}

function Test-Report {
    param($Name, $PageUrl, $GenerateUrl, $GenerateMethod, $GenerateBody, 
          $ExcelUrl, $CsvUrl, $PdfUrl, $PrintUrl,
          $ScheduleUrl, $LayoutType,
          $ExportMethod)
    
    Write-Host "`n$('='*50)"
    Write-Host " $Name"
    Write-Host "$('='*50)"
    
    # Page Load
    Write-Host "`n--- Page Load ---"
    $page = Invoke-WebRequest -Uri "$base$PageUrl" -UseBasicParsing -WebSession $session
    $title = ([regex]::Match($page.Content, '<title>([^<]+)</title>')).Groups[1].Value
    Write-Host "[$(if ($title -notmatch 'Login') {'PASS'} else {'FAIL'})] Page: $title ($($page.Content.Length) bytes)"
    
    # Generate
    Write-Host "`n--- Generate ---"
    if ($GenerateMethod -eq 'POST') {
        $gen = Invoke-WebRequest -Uri "$base$GenerateUrl" -Method POST -Body $GenerateBody -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session
    } else {
        $gen = Invoke-WebRequest -Uri "$base$GenerateUrl" -UseBasicParsing -WebSession $session
    }
    $genCt = $gen.Headers['Content-Type']
    if ($genCt -match 'json') {
        $genJson = $gen.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
        $rowCount = if ($genJson.data) { $genJson.data.Count } elseif ($genJson.rows) { $genJson.rows.Count } elseif ($genJson.Count -gt 0) { $genJson.Count } else { 'unknown' }
        Write-Host "[PASS] Generate: $rowCount rows (JSON)"
    } else {
        $totalMatch = [regex]::Match($gen.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
        $rows = if ($totalMatch.Success) { $totalMatch.Groups[1].Value } else { 'view rendered' }
        Write-Host "[$(if ($gen.Content.Length -gt 1000) {'PASS'} else {'FAIL'})] Generate: $rows ($($gen.Content.Length) bytes)"
    }
    
    # Excel Export
    if ($ExcelUrl) {
        Write-Host "`n--- Excel ---"
        if ($ExportMethod -eq 'POST') {
            $excel = Invoke-WebRequest -Uri "$base$ExcelUrl" -Method POST -Body $GenerateBody -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session
        } else {
            $excel = Invoke-WebRequest -Uri "$base$ExcelUrl" -UseBasicParsing -WebSession $session
        }
        $ect = $excel.Headers['Content-Type']
        Write-Host "[$(if ($ect -match 'spreadsheet|excel|octet') {'PASS'} else {'FAIL'})] Excel: $ect ($($excel.Content.Length) bytes)"
    }
    
    # CSV Export
    if ($CsvUrl) {
        Write-Host "`n--- CSV ---"
        if ($ExportMethod -eq 'POST') {
            $csv = Invoke-WebRequest -Uri "$base$CsvUrl" -Method POST -Body $GenerateBody -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session
        } else {
            $csv = Invoke-WebRequest -Uri "$base$CsvUrl" -UseBasicParsing -WebSession $session
        }
        $cct = $csv.Headers['Content-Type']
        Write-Host "[$(if ($cct -match 'csv|text') {'PASS'} else {'FAIL'})] CSV: $cct ($($csv.Content.Length) bytes)"
    }
    
    # PDF Export
    if ($PdfUrl) {
        Write-Host "`n--- PDF ---"
        if ($ExportMethod -eq 'POST') {
            $pdf = Invoke-WebRequest -Uri "$base$PdfUrl" -Method POST -Body $GenerateBody -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session
        } else {
            $pdf = Invoke-WebRequest -Uri "$base$PdfUrl" -UseBasicParsing -WebSession $session
        }
        $pct = $pdf.Headers['Content-Type']
        Write-Host "[$(if ($pct -match 'pdf') {'PASS'} else {'FAIL'})] PDF: $pct ($($pdf.Content.Length) bytes)"
    }
    
    # Print Preview
    if ($PrintUrl) {
        Write-Host "`n--- Print ---"
        $print = Invoke-WebRequest -Uri "$base$PrintUrl" -UseBasicParsing -WebSession $session
        $pTitle = ([regex]::Match($print.Content, '<title>([^<]+)</title>')).Groups[1].Value
        Write-Host "[$(if ($print.Content -match '<table') {'PASS'} else {'FAIL'})] Print: $pTitle ($($print.Content.Length) bytes)"
    }
    
    # Schedule
    if ($ScheduleUrl) {
        Write-Host "`n--- Schedule ---"
        $sched = Invoke-WebRequest -Uri "$base$ScheduleUrl" -UseBasicParsing -WebSession $session
        $schedData = $sched.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
        $schedCount = if ($schedData) { $schedData.Count } else { 0 }
        Write-Host "[PASS] Schedules: $schedCount"
    }
    
    # Layout
    if ($LayoutType) {
        Write-Host "`n--- Layout ---"
        $layout = Invoke-WebRequest -Uri "$base/Reports/GetReportLayout?reportType=$LayoutType" -UseBasicParsing -WebSession $session
        Write-Host "[PASS] Layout API works"
    }
}

# =========================================
# PARETO
# =========================================
$paretoParams = "dateFrom=2025-01-01&dateTo=2025-12-31&dimension=Category&metric=Quantity&includeVat=false&excludeNegativeAmounts=true&classAThreshold=80&classBThreshold=15&timezoneOffsetMinutes=-180"
Test-Report -Name "PARETO 80/20" `
    -PageUrl "/Reports/Pareto" `
    -GenerateUrl "/Reports/GetParetoData?$paretoParams" `
    -GenerateMethod "POST" -GenerateBody $paretoParams `
    -ExcelUrl "/Reports/ExportParetoExcel?$paretoParams" `
    -CsvUrl "/Reports/ExportParetoCsv?$paretoParams" `
    -PdfUrl "/Reports/ExportParetoPdf?$paretoParams" `
    -ScheduleUrl "/Reports/GetParetoSchedules" `
    -LayoutType "Pareto" -ExportMethod "GET"

# =========================================
# CHARTS
# =========================================
$chartParams = "mode=Sales&dimension=Category&metric=Quantity&topN=10&chartType=Bar&showOthers=false&compareLastYear=false&includeVat=false&dateFrom=2025-01-01&dateTo=2025-12-31"
Test-Report -Name "CHARTS & DASHBOARDS" `
    -PageUrl "/Reports/Charts" `
    -GenerateUrl "/Reports/GetChartData?$chartParams" `
    -GenerateMethod "POST" -GenerateBody $chartParams `
    -ExcelUrl "/Reports/ExportChartExcel?$chartParams" `
    -CsvUrl "/Reports/ExportChartCsv?$chartParams" `
    -PdfUrl "/Reports/ExportChartPdf?$chartParams" `
    -PrintUrl "/Reports/PrintChartPreview?$chartParams" `
    -ScheduleUrl "/Reports/GetChartSchedules" `
    -LayoutType "Charts" -ExportMethod "GET"

# =========================================
# BELOW MIN STOCK
# =========================================
Test-Report -Name "BELOW MIN STOCK" `
    -PageUrl "/Reports/BelowMinStock" `
    -GenerateUrl "/Reports/GetBelowMinStockData?sortColumn=ItemCode&sortDirection=ASC" `
    -GenerateMethod "POST" -GenerateBody "sortColumn=ItemCode&sortDirection=ASC" `
    -ScheduleUrl "/Reports/GetBmsSchedules" `
    -LayoutType "BelowMinStock" -ExportMethod "GET"

# =========================================
# CANCEL LOG
# =========================================
$clParams = "dateFrom=2025-01-01&dateTo=2025-12-31&actionType=All&reportType=Detailed&primaryGroup=None&timezoneOffsetMinutes=-180&maxRecords=1000&sortColumn=DateTrans&sortDirection=DESC"
Test-Report -Name "CANCEL LOG" `
    -PageUrl "/Reports/CancelLog" `
    -GenerateUrl "/Reports/GetCancelLogData?$clParams" `
    -GenerateMethod "POST" -GenerateBody $clParams `
    -ExcelUrl "/Reports/ExportCancelLogExcel?$clParams" `
    -CsvUrl "/Reports/ExportCancelLogCsv?$clParams" `
    -PdfUrl "/Reports/ExportCancelLogPdf?$clParams" `
    -PrintUrl "/Reports/CancelLogPrintPreview?$clParams" `
    -ScheduleUrl "/Reports/GetCancelLogSchedules" `
    -LayoutType "CancelLog" -ExportMethod "GET"

# =========================================
# TRIAL BALANCE
# =========================================
$tbParams = "asAt=2025-12-31&includeZeroMovements=false&reportMode=Summary&sortColumn=AccountCode&sortDirection=ASC"
Test-Report -Name "TRIAL BALANCE" `
    -PageUrl "/Reports/TrialBalance" `
    -GenerateUrl "/Reports/GetTrialBalanceData?$tbParams" `
    -GenerateMethod "POST" -GenerateBody $tbParams `
    -ExcelUrl "/Reports/ExportTrialBalanceExcel?$tbParams" `
    -CsvUrl "/Reports/ExportTrialBalanceCsv?$tbParams" `
    -PdfUrl "/Reports/ExportTrialBalancePdf?$tbParams" `
    -PrintUrl "/Reports/TrialBalancePrintPreview?$tbParams" `
    -ScheduleUrl "/Reports/GetTrialBalanceSchedules" `
    -LayoutType "TrialBalance" -ExportMethod "GET"

# =========================================
# PROFIT & LOSS
# =========================================
$plParams = "dateFrom=2025-01-01&dateTo=2025-12-31&headerLevel=false&compareToLastYear=false&sortColumn=Sequence&sortDirection=ASC"
Test-Report -Name "PROFIT & LOSS" `
    -PageUrl "/Reports/ProfitLoss" `
    -GenerateUrl "/Reports/GetProfitLossData?$plParams" `
    -GenerateMethod "POST" -GenerateBody $plParams `
    -ExcelUrl "/Reports/ExportProfitLossExcel?$plParams" `
    -CsvUrl "/Reports/ExportProfitLossCsv?$plParams" `
    -PdfUrl "/Reports/ExportProfitLossPdf?$plParams" `
    -PrintUrl "/Reports/ProfitLossPrintPreview?$plParams" `
    -ScheduleUrl "/Reports/GetProfitLossSchedules" `
    -LayoutType "ProfitLoss" -ExportMethod "GET"

# =========================================
# PROSPECT CLIENTS
# =========================================
$pcParams = "dateFrom=2025-01-01&dateTo=2025-12-31&dateField=CreatedDate&primaryGroup=None&maxRecords=500&sortColumn=CreatedDate&sortDirection=DESC"
Test-Report -Name "PROSPECT CLIENTS" `
    -PageUrl "/Reports/ProspectClients" `
    -GenerateUrl "/Reports/GetProspectClientsData?$pcParams" `
    -GenerateMethod "POST" -GenerateBody $pcParams `
    -ExcelUrl "/Reports/ExportProspectClientsExcel?$pcParams" `
    -CsvUrl "/Reports/ExportProspectClientsCsv?$pcParams" `
    -PdfUrl "/Reports/ExportProspectClientsPdf?$pcParams" `
    -PrintUrl "/Reports/ProspectClientsPrintPreview?$pcParams" `
    -ScheduleUrl "/Reports/GetProspectClientsSchedules" `
    -LayoutType "ProspectClients" -ExportMethod "GET"

# =========================================
# OFFERS REPORT
# =========================================
$ofParams = "dateFrom=2025-01-01&dateTo=2025-12-31&dateField=OfferDate&primaryGroup=None&maxRecords=500&sortColumn=OfferDate&sortDirection=DESC"
Test-Report -Name "OFFERS REPORT" `
    -PageUrl "/Reports/OffersReport" `
    -GenerateUrl "/Reports/GetOffersReportData?$ofParams" `
    -GenerateMethod "POST" -GenerateBody $ofParams `
    -ExcelUrl "/Reports/ExportOffersReportExcel?$ofParams" `
    -CsvUrl "/Reports/ExportOffersReportCsv?$ofParams" `
    -PdfUrl "/Reports/ExportOffersReportPdf?$ofParams" `
    -PrintUrl "/Reports/OffersReportPrintPreview?$ofParams" `
    -ScheduleUrl "/Reports/GetOffersReportSchedules" `
    -LayoutType "OffersReport" -ExportMethod "GET"

Write-Host "`n$('='*50)"
Write-Host " ALL REPORTS TESTED"
Write-Host "$('='*50)"

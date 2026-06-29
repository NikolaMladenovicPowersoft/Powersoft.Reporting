$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5150'
$cookiePath = 'C:\p\Powersoft.Reporting\_test_cookies.json'

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$cookies = Get-Content $cookiePath -Raw | ConvertFrom-Json
foreach ($c in $cookies) { $session.Cookies.Add($(New-Object System.Uri($base)), (New-Object System.Net.Cookie($c.Name, $c.Value, $c.Path, $c.Domain))) }

$check = (Invoke-WebRequest -Uri "$base/Reports/GetStores" -UseBasicParsing -WebSession $session).Content | ConvertFrom-Json
if ($check.Count -eq 0) {
    $lp = Invoke-WebRequest -Uri "$base/Account/Login" -UseBasicParsing -WebSession $session
    $tk = ([regex]::Match($lp.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    Invoke-WebRequest -Uri "$base/Account/Login" -Method POST -Body "Username=REPORTING_TEST&Password=Test123!&RememberMe=true&__RequestVerificationToken=$([uri]::EscapeDataString($tk))" -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session | Out-Null
    Invoke-WebRequest -Uri "$base/Home/Connect" -Method POST -Body @{databaseCode='DEMO365MODAPRO1'} -UseBasicParsing -WebSession $session | Out-Null
    $allC = $session.Cookies.GetCookies($base); $cl = @(); foreach ($c in $allC) { $cl += @{ Name=$c.Name; Value=$c.Value; Domain=$c.Domain; Path=$c.Path } }; $cl | ConvertTo-Json -Depth 3 | Out-File $cookiePath -Encoding utf8
}

$pass = 0; $fail = 0; $total = 0
function Check($name, $condition) {
    $script:total++
    if ($condition) { $script:pass++; Write-Host "[PASS] $name" }
    else { $script:fail++; Write-Host "[FAIL] $name" }
}

function TestJsonReport {
    param($Name, $PageUrl, $GenerateUrl, $ExcelUrl, $CsvUrl, $PdfUrl, $PrintUrl, $ScheduleUrl, $LayoutType,
          $Controls, $Checkboxes, $GenerateParams)

    Write-Host "`n$('='*60)"
    Write-Host " $Name"
    Write-Host "$('='*60)"

    # PAGE LOAD
    Write-Host "`n--- Page Load ---"
    $page = Invoke-WebRequest -Uri "$base$PageUrl" -UseBasicParsing -WebSession $session
    Check "$Name - Page loads" ($page.StatusCode -eq 200)
    $title = ([regex]::Match($page.Content, '<title>([^<]+)</title>')).Groups[1].Value
    Check "$Name - Title" ($title -notmatch 'Login')
    Write-Host "  Title: $title"

    if ($Controls) {
        foreach ($c in $Controls) { Check "$Name - Control: $c" ($page.Content -match $c) }
    }
    if ($Checkboxes) {
        foreach ($cb in $Checkboxes) { Check "$Name - Checkbox: $cb" ($page.Content -match $cb) }
    }

    Check "$Name - Generate/Schedule/Export buttons" ($page.Content -match 'Generate|generate')
    Check "$Name - Items Selection" ($page.Content -match 'itemsSelection|ItemsSelection|_ItemsSelection' -or $page.Content -match 'filterPanel')

    # GENERATE
    Write-Host "`n--- Generate ---"
    $gen = Invoke-WebRequest -Uri "$base$GenerateUrl" -UseBasicParsing -WebSession $session
    $genJson = $gen.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
    if ($genJson) {
        $rowCount = if ($genJson.data) { $genJson.data.Count } elseif ($genJson.rows) { $genJson.rows.Count } elseif ($genJson.totalRows -ne $null) { $genJson.totalRows } else { $genJson.Count }
        Check "$Name - Generate returns data ($rowCount rows)" ($rowCount -gt 0 -or $gen.Content.Length -gt 50)
    } else {
        Check "$Name - Generate returns content" ($gen.Content.Length -gt 100)
    }

    # EXCEL
    if ($ExcelUrl) {
        Write-Host "`n--- Excel ---"
        $excel = Invoke-WebRequest -Uri "$base$ExcelUrl" -UseBasicParsing -WebSession $session
        Check "$Name - Excel export" ($excel.Headers['Content-Type'] -match 'spreadsheet|excel|octet')
        Write-Host "  Size: $($excel.Content.Length) bytes"
    }

    # CSV
    if ($CsvUrl) {
        Write-Host "`n--- CSV ---"
        $csv = Invoke-WebRequest -Uri "$base$CsvUrl" -UseBasicParsing -WebSession $session
        Check "$Name - CSV export" ($csv.Headers['Content-Type'] -match 'csv|text')
        Write-Host "  Size: $($csv.Content.Length) bytes"
    }

    # PDF
    if ($PdfUrl) {
        Write-Host "`n--- PDF ---"
        $pdf = Invoke-WebRequest -Uri "$base$PdfUrl" -UseBasicParsing -WebSession $session
        Check "$Name - PDF export" ($pdf.Headers['Content-Type'] -match 'pdf')
        Write-Host "  Size: $($pdf.Content.Length) bytes"
    }

    # PRINT
    if ($PrintUrl) {
        Write-Host "`n--- Print ---"
        $print = Invoke-WebRequest -Uri "$base$PrintUrl" -UseBasicParsing -WebSession $session
        Check "$Name - Print preview" ($print.Content -match '<table|<div')
        Write-Host "  Size: $($print.Content.Length) bytes"
    }

    # SCHEDULE
    if ($ScheduleUrl) {
        Write-Host "`n--- Schedule ---"
        $sched = Invoke-WebRequest -Uri "$base$ScheduleUrl" -UseBasicParsing -WebSession $session
        $schedData = $sched.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
        Check "$Name - Schedule API" ($null -ne $schedData)
        Write-Host "  Count: $($schedData.Count)"
    }

    # LAYOUT
    if ($LayoutType) {
        Write-Host "`n--- Layout ---"
        $layout = Invoke-WebRequest -Uri "$base/Reports/GetReportLayout?reportType=$LayoutType" -UseBasicParsing -WebSession $session
        Check "$Name - Layout API" ($layout.StatusCode -eq 200)
    }

    # EMAIL
    if ($LayoutType) {
        Write-Host "`n--- Email ---"
        $email = Invoke-WebRequest -Uri "$base/Reports/GetEmailTemplates?reportType=$LayoutType" -UseBasicParsing -WebSession $session
        $et = $email.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
        Check "$Name - Email templates" ($null -ne $et)
        Write-Host "  Templates: $($et.Count)"
    }
}

# ================================================================
# CATALOGUE
# ================================================================
$catParams = "dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=Summary&reportOn=Sale&primaryGroup=Category&secondaryGroup=None&thirdGroup=None&dateBasis=DateTrans&includeVat=false&showProfit=true&showStock=true&sortColumn=GroupName&sortDirection=ASC"
TestJsonReport -Name "CATALOGUE" `
    -PageUrl "/Reports/Catalogue" `
    -GenerateUrl "/Reports/Catalogue" `
    -ExcelUrl "/Reports/ExportCatalogueExcel?$catParams" `
    -CsvUrl "/Reports/ExportCatalogueCsv?$catParams" `
    -PrintUrl "/Reports/PrintCataloguePreview?$catParams" `
    -ScheduleUrl "/Reports/GetCatalogueSchedules" `
    -LayoutType "Catalogue" `
    -Controls @('DateFrom','DateTo','reportMode','reportOn','primaryGroup','secondaryGroup','thirdGroup','dateBasis') `
    -Checkboxes @('showProfit','showStock','includeVat')

# Additional Catalogue tests
Write-Host "`n--- Catalogue: Extra Tests ---"
foreach ($rOn in @('Sale','Purchase','Both')) {
    $url = "dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=Summary&reportOn=$rOn&primaryGroup=Category&secondaryGroup=None&thirdGroup=None&dateBasis=DateTrans&includeVat=false&showProfit=true&showStock=true&sortColumn=GroupName&sortDirection=ASC"
    $resp = Invoke-WebRequest -Uri "$base/Reports/ExportCatalogueCsv?$url" -UseBasicParsing -WebSession $session
    Check "Catalogue ReportOn=$rOn" ($resp.Headers['Content-Type'] -match 'csv')
}

foreach ($mode in @('Summary','Detail')) {
    $url = "dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=$mode&reportOn=Sale&primaryGroup=Category&secondaryGroup=None&thirdGroup=None&dateBasis=DateTrans&includeVat=false&showProfit=true&showStock=true&sortColumn=GroupName&sortDirection=ASC"
    $resp = Invoke-WebRequest -Uri "$base/Reports/ExportCatalogueCsv?$url" -UseBasicParsing -WebSession $session
    Check "Catalogue Mode=$mode" ($resp.Headers['Content-Type'] -match 'csv')
}

foreach ($gb in @('Category','Store','Brand','Department','Season','Supplier')) {
    $url = "dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=Summary&reportOn=Sale&primaryGroup=$gb&secondaryGroup=None&thirdGroup=None&dateBasis=DateTrans&includeVat=false&showProfit=true&showStock=true&sortColumn=GroupName&sortDirection=ASC"
    $resp = Invoke-WebRequest -Uri "$base/Reports/ExportCatalogueCsv?$url" -UseBasicParsing -WebSession $session
    Check "Catalogue GroupBy=$gb" ($resp.Headers['Content-Type'] -match 'csv')
}

# ================================================================
# PARETO 80/20
# ================================================================
$paretoParams = "dateFrom=2025-01-01&dateTo=2025-12-31&dimension=Category&metric=Quantity&includeVat=false&excludeNegativeAmounts=true&classAThreshold=80&classBThreshold=15&timezoneOffsetMinutes=-180"
TestJsonReport -Name "PARETO 80/20" `
    -PageUrl "/Reports/Pareto" `
    -GenerateUrl "/Reports/GetParetoData?$paretoParams" `
    -ExcelUrl "/Reports/ExportParetoExcel?$paretoParams" `
    -CsvUrl "/Reports/ExportParetoCsv?$paretoParams" `
    -PdfUrl "/Reports/ExportParetoPdf?$paretoParams" `
    -ScheduleUrl "/Reports/GetParetoSchedules" `
    -LayoutType "Pareto" `
    -Controls @('dateFrom','dateTo','dimension','metric') `
    -Checkboxes @('includeVat','excludeNegativeAmounts')

# Pareto: Different dimensions
Write-Host "`n--- Pareto: Dimensions ---"
foreach ($dim in @('Category','Department','Brand','Season','Store','Item','Supplier')) {
    $url = "dateFrom=2025-01-01&dateTo=2025-12-31&dimension=$dim&metric=Quantity&includeVat=false&excludeNegativeAmounts=true&classAThreshold=80&classBThreshold=15&timezoneOffsetMinutes=-180"
    $resp = Invoke-WebRequest -Uri "$base/Reports/GetParetoData?$url" -UseBasicParsing -WebSession $session
    $data = $resp.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
    $cnt = if ($data.data) { $data.data.Count } else { 0 }
    Check "Pareto Dimension=$dim ($cnt)" ($cnt -gt 0 -or $resp.Content.Length -gt 50)
}

# Pareto: Different metrics
Write-Host "`n--- Pareto: Metrics ---"
foreach ($m in @('Quantity','Value','Profit')) {
    $url = "dateFrom=2025-01-01&dateTo=2025-12-31&dimension=Category&metric=$m&includeVat=false&excludeNegativeAmounts=true&classAThreshold=80&classBThreshold=15&timezoneOffsetMinutes=-180"
    $resp = Invoke-WebRequest -Uri "$base/Reports/GetParetoData?$url" -UseBasicParsing -WebSession $session
    Check "Pareto Metric=$m" ($resp.Content.Length -gt 50)
}

# ================================================================
# CHARTS & DASHBOARDS
# ================================================================
$chartParams = "mode=Sales&dimension=Category&metric=Quantity&topN=10&chartType=Bar&showOthers=false&compareLastYear=false&includeVat=false&dateFrom=2025-01-01&dateTo=2025-12-31"
TestJsonReport -Name "CHARTS" `
    -PageUrl "/Reports/Charts" `
    -GenerateUrl "/Reports/GetChartData?$chartParams" `
    -ExcelUrl "/Reports/ExportChartExcel?$chartParams" `
    -CsvUrl "/Reports/ExportChartCsv?$chartParams" `
    -PdfUrl "/Reports/ExportChartPdf?$chartParams" `
    -PrintUrl "/Reports/PrintChartPreview?$chartParams" `
    -ScheduleUrl "/Reports/GetChartSchedules" `
    -LayoutType "Charts" `
    -Controls @('mode','dimension','metric','topN','chartType') `
    -Checkboxes @('showOthers','compareLastYear','includeVat')

# Charts: Different modes + chart types
Write-Host "`n--- Charts: Modes & Types ---"
foreach ($m in @('Sales','Purchases','Both')) {
    $url = "mode=$m&dimension=Category&metric=Quantity&topN=10&chartType=Bar&showOthers=false&compareLastYear=false&includeVat=false&dateFrom=2025-01-01&dateTo=2025-12-31"
    $resp = Invoke-WebRequest -Uri "$base/Reports/GetChartData?$url" -UseBasicParsing -WebSession $session
    Check "Charts Mode=$m" ($resp.Content.Length -gt 50)
}
foreach ($ct in @('Bar','Pie','Line','Doughnut')) {
    $url = "mode=Sales&dimension=Category&metric=Quantity&topN=10&chartType=$ct&showOthers=false&compareLastYear=false&includeVat=false&dateFrom=2025-01-01&dateTo=2025-12-31"
    $resp = Invoke-WebRequest -Uri "$base/Reports/GetChartData?$url" -UseBasicParsing -WebSession $session
    Check "Charts ChartType=$ct" ($resp.Content.Length -gt 50)
}

# ================================================================
# CANCEL LOG
# ================================================================
$clParams = "dateFrom=2025-01-01&dateTo=2025-12-31&actionType=All&reportType=Detailed&primaryGroup=None&timezoneOffsetMinutes=-180&maxRecords=1000&sortColumn=DateTrans&sortDirection=DESC"
TestJsonReport -Name "CANCEL LOG" `
    -PageUrl "/Reports/CancelLog" `
    -GenerateUrl "/Reports/GetCancelLogData?$clParams" `
    -ExcelUrl "/Reports/ExportCancelLogExcel?$clParams" `
    -CsvUrl "/Reports/ExportCancelLogCsv?$clParams" `
    -PdfUrl "/Reports/ExportCancelLogPdf?$clParams" `
    -PrintUrl "/Reports/CancelLogPrintPreview?$clParams" `
    -ScheduleUrl "/Reports/GetCancelLogSchedules" `
    -LayoutType "CancelLog" `
    -Controls @('dateFrom','dateTo','actionType','reportType','primaryGroup')

# Cancel Log: Action types
Write-Host "`n--- CancelLog: Action Types ---"
foreach ($at in @('All','Cancel','Void','Return')) {
    $url = "dateFrom=2025-01-01&dateTo=2025-12-31&actionType=$at&reportType=Detailed&primaryGroup=None&timezoneOffsetMinutes=-180&maxRecords=1000&sortColumn=DateTrans&sortDirection=DESC"
    $resp = Invoke-WebRequest -Uri "$base/Reports/GetCancelLogData?$url" -UseBasicParsing -WebSession $session
    $data = $resp.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
    $cnt = if ($data.data) { $data.data.Count } elseif ($data.rows) { $data.rows.Count } else { $data.Count }
    Check "CancelLog ActionType=$at ($cnt)" ($resp.Content.Length -gt 10)
}

# ================================================================
# BELOW MIN STOCK
# ================================================================
TestJsonReport -Name "BELOW MIN STOCK" `
    -PageUrl "/Reports/BelowMinStock" `
    -GenerateUrl "/Reports/GetBelowMinStockData?sortColumn=ItemCode&sortDirection=ASC" `
    -ScheduleUrl "/Reports/GetBmsSchedules" `
    -LayoutType "BelowMinStock" `
    -Controls @('sortColumn','sortDirection')

# ================================================================
# PROSPECT CLIENTS
# ================================================================
$pcParams = "dateFrom=2025-01-01&dateTo=2025-12-31&dateField=CreatedDate&primaryGroup=None&maxRecords=500&sortColumn=CreatedDate&sortDirection=DESC"
TestJsonReport -Name "PROSPECT CLIENTS" `
    -PageUrl "/Reports/ProspectClients" `
    -GenerateUrl "/Reports/GetProspectClientsData?$pcParams" `
    -ExcelUrl "/Reports/ExportProspectClientsExcel?$pcParams" `
    -CsvUrl "/Reports/ExportProspectClientsCsv?$pcParams" `
    -PdfUrl "/Reports/ExportProspectClientsPdf?$pcParams" `
    -PrintUrl "/Reports/ProspectClientsPrintPreview?$pcParams" `
    -ScheduleUrl "/Reports/GetProspectClientsSchedules" `
    -LayoutType "ProspectClients" `
    -Controls @('dateFrom','dateTo','dateField','primaryGroup')

# ================================================================
# OFFERS REPORT
# ================================================================
$ofParams = "dateFrom=2025-01-01&dateTo=2025-12-31&dateField=OfferDate&primaryGroup=None&maxRecords=500&sortColumn=OfferDate&sortDirection=DESC"
TestJsonReport -Name "OFFERS REPORT" `
    -PageUrl "/Reports/OffersReport" `
    -GenerateUrl "/Reports/GetOffersReportData?$ofParams" `
    -ExcelUrl "/Reports/ExportOffersReportExcel?$ofParams" `
    -CsvUrl "/Reports/ExportOffersReportCsv?$ofParams" `
    -PdfUrl "/Reports/ExportOffersReportPdf?$ofParams" `
    -PrintUrl "/Reports/OffersReportPrintPreview?$ofParams" `
    -ScheduleUrl "/Reports/GetOffersReportSchedules" `
    -LayoutType "OffersReport" `
    -Controls @('dateFrom','dateTo','dateField','primaryGroup')

# ================================================================
# TRIAL BALANCE
# ================================================================
$tbParams = "asAt=2025-12-31&includeZeroMovements=false&reportMode=Summary&sortColumn=AccountCode&sortDirection=ASC"
TestJsonReport -Name "TRIAL BALANCE" `
    -PageUrl "/Reports/TrialBalance" `
    -GenerateUrl "/Reports/GetTrialBalanceData?$tbParams" `
    -ExcelUrl "/Reports/ExportTrialBalanceExcel?$tbParams" `
    -CsvUrl "/Reports/ExportTrialBalanceCsv?$tbParams" `
    -PdfUrl "/Reports/ExportTrialBalancePdf?$tbParams" `
    -PrintUrl "/Reports/TrialBalancePrintPreview?$tbParams" `
    -ScheduleUrl "/Reports/GetTrialBalanceSchedules" `
    -LayoutType "TrialBalance" `
    -Controls @('asAt','includeZeroMovements','reportMode')

# ================================================================
# PROFIT & LOSS
# ================================================================
$plParams = "dateFrom=2025-01-01&dateTo=2025-12-31&headerLevel=false&compareToLastYear=false&sortColumn=Sequence&sortDirection=ASC"
TestJsonReport -Name "PROFIT & LOSS" `
    -PageUrl "/Reports/ProfitLoss" `
    -GenerateUrl "/Reports/GetProfitLossData?$plParams" `
    -ExcelUrl "/Reports/ExportProfitLossExcel?$plParams" `
    -CsvUrl "/Reports/ExportProfitLossCsv?$plParams" `
    -PdfUrl "/Reports/ExportProfitLossPdf?$plParams" `
    -PrintUrl "/Reports/ProfitLossPrintPreview?$plParams" `
    -ScheduleUrl "/Reports/GetProfitLossSchedules" `
    -LayoutType "ProfitLoss" `
    -Controls @('dateFrom','dateTo')

# P&L: Compare to last year
Write-Host "`n--- P&L: Compare to Last Year ---"
$plLy = "dateFrom=2025-01-01&dateTo=2025-12-31&headerLevel=false&compareToLastYear=true&sortColumn=Sequence&sortDirection=ASC"
$resp = Invoke-WebRequest -Uri "$base/Reports/GetProfitLossData?$plLy" -UseBasicParsing -WebSession $session
Check "P&L CompareToLastYear=true" ($resp.Content.Length -gt 50)

Write-Host "`n$('='*60)"
Write-Host " GRAND TOTAL: $pass PASS / $fail FAIL / $total TOTAL"
Write-Host "$('='*60)"

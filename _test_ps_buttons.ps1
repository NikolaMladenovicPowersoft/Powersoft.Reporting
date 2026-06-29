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

function PSGenerate($params) {
    $page = Invoke-WebRequest -Uri "$base/Reports/PurchasesSales" -UseBasicParsing -WebSession $session
    $token = ([regex]::Match($page.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    $defaults = @{
        DateFrom='01/01/2025'; DateTo='31/12/2025'
        ReportMode='Summary'; PrimaryGroup='None'; SecondaryGroup='None'; ThirdGroup='None'
        IncludeVat='false'; ShowProfit='true'; ShowStock='false'
        ShowOnOrder='false'; ShowReservation='false'; ShowAvailable='false'
        IncludeAdditionalCharges='true'
        PageNumber='1'; PageSize='50'
        SortColumn='ItemCode'; SortDirection='ASC'
        __RequestVerificationToken = $token
    }
    foreach ($k in $params.Keys) { $defaults[$k] = $params[$k] }
    return Invoke-WebRequest -Uri "$base/Reports/PurchasesSales" -Method POST -Body $defaults -UseBasicParsing -WebSession $session
}

$psExportBase = "dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=Summary&primaryGroup=None&secondaryGroup=None&thirdGroup=None&includeVat=false&showProfit=true&showStock=false&showOnOrder=false&showReservation=false&showAvailable=false&includeAdditionalCharges=true&sortColumn=ItemCode&sortDirection=ASC"

Write-Host "============================================================"
Write-Host " PURCHASES VS SALES - FULL BUTTON-BY-BUTTON VERIFICATION"
Write-Host "============================================================"

# ==========================================================================
# 1. PAGE LOAD
# ==========================================================================
Write-Host "`n=== 1. PAGE LOAD ==="
$page = Invoke-WebRequest -Uri "$base/Reports/PurchasesSales" -UseBasicParsing -WebSession $session
Check "Page loads (200)" ($page.StatusCode -eq 200)
$title = ([regex]::Match($page.Content, '<title>([^<]+)</title>')).Groups[1].Value
Check "Title correct" ($title -match 'Purchases.*Sales')

# Form controls
foreach ($ctrl in @('DateFrom','DateTo','ReportMode','PrimaryGroup','SecondaryGroup','ThirdGroup')) {
    Check "Control: $ctrl" ($page.Content -match "name=`"$ctrl`"")
}
foreach ($chk in @('IncludeVat','ShowProfit','ShowStock','ShowOnOrder','ShowReservation','ShowAvailable','IncludeAdditionalCharges')) {
    Check "Checkbox: $chk" ($page.Content -match $chk)
}

# Buttons
Check "Generate button present" ($page.Content -match 'generateBtn|Generate')
Check "Schedule button present" ($page.Content -match 'scheduleModal|Schedule')
Check "Layouts dropdown present" ($page.Content -match 'layoutsDropdownBtn|Layouts')
Check "Save button present" ($page.Content -match 'saveCurrentLayout|Save')
Check "Columns button present" ($page.Content -match 'columnSettingsModal|Columns')
Check "Items Selection present" ($page.Content -match 'itemsSelection|ItemsSelection')
Check "Empty state shown" ($page.Content -match 'Generate Your Report|Generate.*Report')

# Date presets
foreach ($dp in @('Today','Yesterday','Last 7','Last 30','This Month','Last Month','Year to Date','Last Year')) {
    Check "Date preset: $dp" ($page.Content -match [regex]::Escape($dp))
}

# ==========================================================================
# 2. GENERATE - Default (Summary, no grouping)
# ==========================================================================
Write-Host "`n=== 2. GENERATE - Default ==="
$gen1 = PSGenerate @{}
Check "Generate returns data" ($gen1.Content.Length -gt 10000)
$totalMatch = [regex]::Match($gen1.Content, 'Showing\s+\d+\s+to\s+\d+\s+of\s+(\d+)')
if ($totalMatch.Success) { Write-Host "  Total rows: $($totalMatch.Groups[1].Value)" }
Check "Results table present" ($gen1.Content -match '<table|resultsTable')
Check "Grand Total row present" ($gen1.Content -match 'GRAND TOTAL')
Check "Stat cards present" ($gen1.Content -match 'stat-card')
Check "Export buttons appear" ($gen1.Content -match 'ExportPs')
Check "Send Email button" ($gen1.Content -match 'SendEmail|sendEmail|openSendEmailModal')
Check "AI Analyze button" ($gen1.Content -match 'openAiAnalysis|aiAnalyze|AnalyzePs')
Check "Print Preview button" ($gen1.Content -match 'PrintPsPreview|printPsPreview')

# ==========================================================================
# 3. REPORT MODE (Summary vs Detailed)
# ==========================================================================
Write-Host "`n=== 3. REPORT MODE ==="
$genSummary = PSGenerate @{ ReportMode = 'Summary' }
Check "Summary mode works" ($genSummary.Content.Length -gt 5000)

$genDetailed = PSGenerate @{ ReportMode = 'Detailed' }
Check "Detailed mode works" ($genDetailed.Content.Length -gt 5000)

# ==========================================================================
# 4. GROUP BY OPTIONS (PrimaryGroup)
# ==========================================================================
Write-Host "`n=== 4. PRIMARY GROUP BY ==="
foreach ($gb in @('None','Store','Category','Department','Brand','Season','Supplier','Customer','Item')) {
    $gen = PSGenerate @{ PrimaryGroup = $gb; PageSize = '25' }
    $hasData = $gen.Content.Length -gt 5000
    $rows = [regex]::Match($gen.Content, 'of\s+(\d+)')
    $cnt = if ($rows.Success) { $rows.Groups[1].Value } else { 'all' }
    Check "PrimaryGroup=$gb ($cnt rows)" $hasData
}

# ==========================================================================
# 5. SECONDARY + THIRD GROUP BY
# ==========================================================================
Write-Host "`n=== 5. SECONDARY + THIRD GROUP ==="
$gen2g = PSGenerate @{ PrimaryGroup = 'Category'; SecondaryGroup = 'Store' }
Check "Category + Store double grouping" ($gen2g.Content.Length -gt 5000)

$gen3g = PSGenerate @{ PrimaryGroup = 'Category'; SecondaryGroup = 'Store'; ThirdGroup = 'Brand' }
Check "Category + Store + Brand triple grouping" ($gen3g.Content.Length -gt 5000)

# ==========================================================================
# 6. INCLUDE VAT
# ==========================================================================
Write-Host "`n=== 6. INCLUDE VAT ==="
$genNoVat = PSGenerate @{ IncludeVat = 'false' }
$genVat = PSGenerate @{ IncludeVat = 'true' }
Check "No VAT generates" ($genNoVat.Content.Length -gt 5000)
Check "With VAT generates" ($genVat.Content.Length -gt 5000)
Check "Values differ with VAT" ($genNoVat.Content.Length -ne $genVat.Content.Length)

# ==========================================================================
# 7. SHOW PROFIT / SHOW STOCK / SHOW ON ORDER / etc.
# ==========================================================================
Write-Host "`n=== 7. DISPLAY OPTIONS ==="
$genProfit = PSGenerate @{ ShowProfit = 'true' }
Check "ShowProfit=true works" ($genProfit.Content.Length -gt 5000)
$hasProfit = $genProfit.Content -match 'Profit|profit'
Check "Profit column visible" $hasProfit

$genStock = PSGenerate @{ ShowStock = 'true' }
Check "ShowStock=true works" ($genStock.Content.Length -gt 5000)
$hasStock = $genStock.Content -match 'Stock|stock'
Check "Stock column visible" $hasStock

$genOnOrder = PSGenerate @{ ShowOnOrder = 'true' }
Check "ShowOnOrder=true works" ($genOnOrder.Content.Length -gt 5000)

$genAvail = PSGenerate @{ ShowAvailable = 'true'; ShowStock = 'true' }
Check "ShowAvailable=true works" ($genAvail.Content.Length -gt 5000)

$genNoCharges = PSGenerate @{ IncludeAdditionalCharges = 'false' }
Check "IncludeAdditionalCharges=false works" ($genNoCharges.Content.Length -gt 5000)

# ==========================================================================
# 8. PAGINATION
# ==========================================================================
Write-Host "`n=== 8. PAGINATION ==="
$genP1 = PSGenerate @{ PageSize = '25'; PageNumber = '1' }
$showP1 = [regex]::Match($genP1.Content, 'Showing\s+(\d+)\s+to\s+(\d+)\s+of\s+(\d+)')
if ($showP1.Success) {
    Write-Host "  Page 1: Showing $($showP1.Groups[1].Value) to $($showP1.Groups[2].Value) of $($showP1.Groups[3].Value)"
    Check "Pagination page 1" $true
    $hasPgTotal = $genP1.Content -match 'PAGE TOTAL'
    Check "PAGE TOTAL row" $hasPgTotal
}

$genP2 = PSGenerate @{ PageSize = '25'; PageNumber = '2' }
$showP2 = [regex]::Match($genP2.Content, 'Showing\s+26\s+to')
Check "Pagination page 2" ($showP2.Success)

$genP100 = PSGenerate @{ PageSize = '100'; PageNumber = '1' }
$showP100 = [regex]::Match($genP100.Content, 'Showing\s+1\s+to\s+100')
Check "PageSize=100" ($showP100.Success)

# ==========================================================================
# 9. SORTING
# ==========================================================================
Write-Host "`n=== 9. SORTING ==="
foreach ($col in @('ItemCode','ItemName','QuantityPurchased','NetPurchasedValue','QuantitySold','NetSoldValue','Profit','TotalStockQty')) {
    $gen = PSGenerate @{ SortColumn = $col; SortDirection = 'ASC'; PageSize = '25' }
    Check "Sort $col ASC" ($gen.Content.Length -gt 5000)
    $genD = PSGenerate @{ SortColumn = $col; SortDirection = 'DESC'; PageSize = '25' }
    Check "Sort $col DESC" ($genD.Content.Length -gt 5000)
}

# ==========================================================================
# 10. EXPORTS (POST)
# ==========================================================================
Write-Host "`n=== 10. EXPORT BUTTONS ==="
$exportBody = @{
    dateFrom='2025-01-01'; dateTo='2025-12-31'
    reportMode='Summary'; primaryGroup='None'; secondaryGroup='None'; thirdGroup='None'
    includeVat='false'; showProfit='true'; showStock='false'
    showOnOrder='false'; showReservation='false'; showAvailable='false'
    includeAdditionalCharges='true'
    sortColumn='ItemCode'; sortDirection='ASC'
}

# Excel
$excel = Invoke-WebRequest -Uri "$base/Reports/ExportPsExcel" -Method POST -Body $exportBody -UseBasicParsing -WebSession $session
Check "Excel export returns xlsx" ($excel.Headers['Content-Type'] -match 'spreadsheet|excel')
Check "Excel has content" ($excel.Content.Length -gt 100)
Write-Host "  Excel size: $($excel.Content.Length) bytes"

# CSV
$csv = Invoke-WebRequest -Uri "$base/Reports/ExportPsCsv" -Method POST -Body $exportBody -UseBasicParsing -WebSession $session
Check "CSV export returns csv" ($csv.Headers['Content-Type'] -match 'csv')
Check "CSV has content" ($csv.Content.Length -gt 100)
Write-Host "  CSV size: $($csv.Content.Length) bytes"

# PDF
$pdf = Invoke-WebRequest -Uri "$base/Reports/ExportPsPdf" -Method POST -Body $exportBody -UseBasicParsing -WebSession $session
Check "PDF export returns pdf" ($pdf.Headers['Content-Type'] -match 'pdf')
Check "PDF has content" ($pdf.Content.Length -gt 100)
Write-Host "  PDF size: $($pdf.Content.Length) bytes"

# Export with GroupBy
$exportGrouped = $exportBody.Clone(); $exportGrouped['primaryGroup'] = 'Category'
$csvG = Invoke-WebRequest -Uri "$base/Reports/ExportPsCsv" -Method POST -Body $exportGrouped -UseBasicParsing -WebSession $session
Check "CSV with PrimaryGroup=Category" ($csvG.Headers['Content-Type'] -match 'csv')

# Export with VAT
$exportVat = $exportBody.Clone(); $exportVat['includeVat'] = 'true'
$csvV = Invoke-WebRequest -Uri "$base/Reports/ExportPsCsv" -Method POST -Body $exportVat -UseBasicParsing -WebSession $session
Check "CSV with IncludeVat=true" ($csvV.Headers['Content-Type'] -match 'csv')

# Export without additional charges
$exportNoChg = $exportBody.Clone(); $exportNoChg['includeAdditionalCharges'] = 'false'
$csvNc = Invoke-WebRequest -Uri "$base/Reports/ExportPsCsv" -Method POST -Body $exportNoChg -UseBasicParsing -WebSession $session
Check "CSV without AdditionalCharges" ($csvNc.Headers['Content-Type'] -match 'csv')

# ==========================================================================
# 11. PRINT PREVIEW
# ==========================================================================
Write-Host "`n=== 11. PRINT PREVIEW ==="
$print = Invoke-WebRequest -Uri "$base/Reports/PrintPsPreview" -Method POST -Body $exportBody -UseBasicParsing -WebSession $session
Check "Print preview loads" ($print.StatusCode -eq 200)
$printTitle = ([regex]::Match($print.Content, '<title>([^<]+)</title>')).Groups[1].Value
Check "Print preview title" ($printTitle -match 'Print|Preview|Purchase')
Check "Print preview has table" ($print.Content -match '<table')
Check "Print preview has GRAND TOTAL" ($print.Content -match 'GRAND TOTAL')
Write-Host "  Print size: $($print.Content.Length) bytes"

# Print with grouping
$printGrouped = $exportBody.Clone(); $printGrouped['primaryGroup'] = 'Store'
$printG = Invoke-WebRequest -Uri "$base/Reports/PrintPsPreview" -Method POST -Body $printGrouped -UseBasicParsing -WebSession $session
Check "Print with PrimaryGroup=Store" ($printG.Content -match '<table')

# ==========================================================================
# 12. SCHEDULE
# ==========================================================================
Write-Host "`n=== 12. SCHEDULE ==="
$schedList = Invoke-WebRequest -Uri "$base/Reports/GetPsSchedules" -UseBasicParsing -WebSession $session
$schedules = $schedList.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "GetPsSchedules returns array" ($null -ne $schedules)
Write-Host "  Schedules: $($schedules.Count)"

# ==========================================================================
# 13. LAYOUT
# ==========================================================================
Write-Host "`n=== 13. LAYOUT ==="
$layout = Invoke-WebRequest -Uri "$base/Reports/GetReportLayout?reportType=PurchasesSales" -UseBasicParsing -WebSession $session
$layoutJson = $layout.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "GetReportLayout works" ($null -ne $layoutJson)
Write-Host "  Has saved: $($layoutJson.hasSaved)"

# ==========================================================================
# 14. EMAIL
# ==========================================================================
Write-Host "`n=== 14. EMAIL ==="
$emailT = Invoke-WebRequest -Uri "$base/Reports/GetEmailTemplates?reportType=PurchasesSales" -UseBasicParsing -WebSession $session
$templates = $emailT.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "Email templates" ($null -ne $templates)
Write-Host "  Templates: $($templates.Count)"

# ==========================================================================
# 15. AI ANALYZE
# ==========================================================================
Write-Host "`n=== 15. AI ==="
$ai = Invoke-WebRequest -Uri "$base/Reports/GetAiStatus" -UseBasicParsing -WebSession $session
$aiJson = $ai.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "AI configured" ($aiJson.configured -eq $true)

# ==========================================================================
# 16. STORE FILTER
# ==========================================================================
Write-Host "`n=== 16. STORE FILTER ==="
$genStore = PSGenerate @{ SelectedStoreCodesString = '001' }
Check "Store filter single" ($genStore.Content.Length -gt 5000)
$storeFiltered = $genStore.Content -match 'store'
Check "Store filter active indicator" $storeFiltered

# ==========================================================================
# 17. ITEMS SELECTION FILTER
# ==========================================================================
Write-Host "`n=== 17. ITEMS SELECTION ==="
$itemsJson = '{"categories":["ACCESORIES"],"departments":[],"brands":[],"seasons":[],"suppliers":[],"items":[],"stores":[]}'
$genItems = PSGenerate @{ ItemsSelectionJson = $itemsJson }
Check "Items selection (ACCESORIES) works" ($genItems.Content.Length -gt 5000)

# ==========================================================================
# 18. EDGE CASES
# ==========================================================================
Write-Host "`n=== 18. EDGE CASES ==="
$genEmpty = PSGenerate @{ DateFrom='01/01/2000'; DateTo='31/01/2000' }
$noData = $genEmpty.Content -match 'No Data|No.*Found|no.*transactions'
Check "Empty date range" $noData

$genFuture = PSGenerate @{ DateFrom='01/01/2030'; DateTo='31/12/2030' }
$noDataF = $genFuture.Content -match 'No Data|No.*Found'
Check "Future dates" $noDataF

$genSingleDay = PSGenerate @{ DateFrom='14/01/2025'; DateTo='14/01/2025' }
Check "Single day" ($genSingleDay.Content.Length -gt 5000)

# ==========================================================================
Write-Host "`n============================================================"
Write-Host " RESULTS: $pass PASS / $fail FAIL / $total TOTAL"
Write-Host "============================================================"

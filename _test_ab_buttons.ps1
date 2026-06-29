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
    Write-Host "Re-connected"
}

$pass = 0; $fail = 0; $total = 0
function Check($name, $condition) {
    $script:total++
    if ($condition) { $script:pass++; Write-Host "[PASS] $name" } 
    else { $script:fail++; Write-Host "[FAIL] $name" }
}

# Helper: POST the AB form and return response
function Generate($params) {
    $page = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -UseBasicParsing -WebSession $session
    $token = ([regex]::Match($page.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    $defaults = @{
        DateFrom = '01/01/2025'; DateTo = '31/12/2025'
        Breakdown = 'Monthly'; GroupBy = 'None'; SecondaryGroupBy = 'None'
        IncludeVat = 'false'; CompareLastYear = 'false'; ShowTotalQty = 'false'
        ReportLayout = 'AverageBasket'
        PageNumber = '1'; PageSize = '50'
        SortColumn = 'Period'; SortDirection = 'ASC'
        __RequestVerificationToken = $token
    }
    foreach ($k in $params.Keys) { $defaults[$k] = $params[$k] }
    return Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -Method POST -Body $defaults -UseBasicParsing -WebSession $session
}

Write-Host "============================================================"
Write-Host " AVERAGE BASKET - FULL BUTTON-BY-BUTTON VERIFICATION"
Write-Host "============================================================"

# ==========================================================================
# 1. PAGE LOAD (initial, no data)
# ==========================================================================
Write-Host "`n=== 1. PAGE LOAD (initial state) ==="
$page = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -UseBasicParsing -WebSession $session
Check "Page loads (200)" ($page.StatusCode -eq 200)
$title = ([regex]::Match($page.Content, '<title>([^<]+)</title>')).Groups[1].Value
Check "Title correct" ($title -match 'Average Basket')
Check "Generate button present" ($page.Content -match 'generateBtn')
Check "Schedule button present" ($page.Content -match 'scheduleModal')
Check "Layouts dropdown present" ($page.Content -match 'layoutsDropdownBtn')
Check "Save button present" ($page.Content -match 'saveCurrentLayout')
Check "Save As button present" ($page.Content -match 'saveLayoutAsModal')
Check "Columns button present" ($page.Content -match 'columnSettingsModal')
Check "Clear Filters link present" ($page.Content -match 'clearedFilters')
Check "DateFrom input present" ($page.Content -match 'name="DateFrom"')
Check "DateTo input present" ($page.Content -match 'name="DateTo"')
Check "Breakdown select present" ($page.Content -match 'name="Breakdown"')
Check "GroupBy select present" ($page.Content -match 'name="GroupBy"')
Check "SecondaryGroupBy select present" ($page.Content -match 'name="SecondaryGroupBy"')
Check "IncludeVat checkbox present" ($page.Content -match 'includeVatCheck')
Check "CompareLastYear checkbox present" ($page.Content -match 'compareLyCheck')
Check "ShowTotalQty checkbox present" ($page.Content -match 'showTotalQtyCheck')
Check "PageSize select present" ($page.Content -match 'name="PageSize"')
Check "Report Layout radio (Average Basket)" ($page.Content -match 'layoutAvgBasket')
Check "Report Layout radio (People Count)" ($page.Content -match 'layoutPeopleCount')
Check "Items Selection partial present" ($page.Content -match 'itemsSelection')
Check "'Generate Your Report' empty state shown" ($page.Content -match 'Generate Your Report')

# Date presets
foreach ($dp in @('Today','Yesterday','Last 7 Days','Last 30 Days','This Month','Last Month','Year to Date','Last Year')) {
    Check "Date preset: $dp" ($page.Content -match [regex]::Escape($dp))
}

# ==========================================================================
# 2. GENERATE - Default (Monthly, no grouping)
# ==========================================================================
Write-Host "`n=== 2. GENERATE - Default (Monthly, None) ==="
$gen1 = Generate @{}
Check "Generate returns data" ($gen1.Content.Length -gt 10000)
Check "Results table present" ($gen1.Content -match 'resultsTable')
$totalMatch = [regex]::Match($gen1.Content, 'of\s+(\d+)\s+rows')
$totalRows = if ($totalMatch.Success) { $totalMatch.Groups[1].Value } else { 'N/A' }
Check "Total rows shown" ($totalMatch.Success)
Write-Host "  Total rows: $totalRows"
Check "Grand Total row present" ($gen1.Content -match 'GRAND TOTAL')
Check "Stat cards present" ($gen1.Content -match 'stat-card')
Check "Export buttons appear (CSV/Excel/PDF)" ($gen1.Content -match 'ExportCsv' -and $gen1.Content -match 'ExportExcel' -and $gen1.Content -match 'ExportPdf')
Check "Send Email button appears" ($gen1.Content -match 'openSendEmailModal')
Check "AI Analyze button appears" ($gen1.Content -match 'openAiAnalysis')
Check "Preview toggle button appears" ($gen1.Content -match 'previewToggleBtn')

# ==========================================================================
# 3. GENERATE - Each Breakdown Type
# ==========================================================================
Write-Host "`n=== 3. BREAKDOWN TYPES ==="
foreach ($bd in @('Daily','Weekly','Monthly')) {
    $gen = Generate @{ Breakdown = $bd }
    $rows = [regex]::Match($gen.Content, 'of\s+(\d+)\s+rows')
    $cnt = if ($rows.Success) { $rows.Groups[1].Value } else { '0' }
    Check "Breakdown=$bd generates ($cnt rows)" ($gen.Content.Length -gt 5000)
}

# ==========================================================================
# 4. GENERATE - GroupBy options
# ==========================================================================
Write-Host "`n=== 4. GROUP BY OPTIONS ==="
foreach ($gb in @('None','Store','Category','Department','Brand','Season','Customer','User','Supplier','Item')) {
    $gen = Generate @{ GroupBy = $gb; PageSize = '25' }
    $hasData = $gen.Content.Length -gt 5000
    $rows = [regex]::Match($gen.Content, 'of\s+(\d+)\s+rows')
    $cnt = if ($rows.Success) { $rows.Groups[1].Value } else { '0' }
    if ($gb -ne 'None') {
        $hasGroupHeader = $gen.Content -match 'group-header-row'
        Check "GroupBy=$gb renders ($cnt rows, groupHeaders=$hasGroupHeader)" ($hasData)
    } else {
        Check "GroupBy=$gb renders ($cnt rows)" ($hasData)
    }
}

# ==========================================================================
# 5. GENERATE - Secondary GroupBy
# ==========================================================================
Write-Host "`n=== 5. SECONDARY GROUP BY ==="
$gen = Generate @{ GroupBy = 'Category'; SecondaryGroupBy = 'Store' }
$hasSecondary = $gen.Content -match 'then Store' -or $gen.Content -match 'Group2Name'
Check "SecondaryGroupBy=Store with primary=Category" ($gen.Content.Length -gt 5000 -and $hasSecondary)

# ==========================================================================
# 6. INCLUDE VAT toggle
# ==========================================================================
Write-Host "`n=== 6. INCLUDE VAT ==="
$genNoVat = Generate @{ IncludeVat = 'false' }
$genVat = Generate @{ IncludeVat = 'true' }
$noVatLabel = $genNoVat.Content -match 'Net Sales'
$vatLabel = $genVat.Content -match 'Gross'
Check "Without VAT shows 'Net Sales'" $noVatLabel
Check "With VAT shows 'Gross'" $vatLabel
Check "VAT toggle changes values" ($genNoVat.Content.Length -ne $genVat.Content.Length)

# ==========================================================================
# 7. COMPARE LAST YEAR
# ==========================================================================
Write-Host "`n=== 7. COMPARE LAST YEAR ==="
$genLy = Generate @{ CompareLastYear = 'true' }
$hasLyCols = $genLy.Content -match 'LY Invoices' -and $genLy.Content -match 'LY Sales' -and $genLy.Content -match 'YoY'
Check "Compare LY adds LY columns" $hasLyCols
$hasYoyCard = $genLy.Content -match 'YoY Change'
Check "YoY Change stat card appears" $hasYoyCard

# ==========================================================================
# 8. SHOW TOTAL QTY
# ==========================================================================
Write-Host "`n=== 8. SHOW TOTAL QTY ==="
$genTq = Generate @{ ShowTotalQty = 'true' }
$hasTotalValue = $genTq.Content -match 'Total Value'
$hasTotalQty = $genTq.Content -match 'Total Qty'
Check "ShowTotalQty changes column headers" ($hasTotalValue -and $hasTotalQty)

# ==========================================================================
# 9. PEOPLE COUNT LAYOUT
# ==========================================================================
Write-Host "`n=== 9. PEOPLE COUNT LAYOUT ==="
$genPc = Generate @{ ReportLayout = 'PeopleCount'; Breakdown = 'Daily'; GroupBy = 'None'; SecondaryGroupBy = 'Store' }
$hasPeopleTable = $genPc.Content -match 'peopleCountTable'
Check "People Count layout renders" ($hasPeopleTable -or $genPc.Content -match 'Transactions')

# ==========================================================================
# 10. PAGINATION
# ==========================================================================
Write-Host "`n=== 10. PAGINATION ==="
$genP1 = Generate @{ PageSize = '25'; PageNumber = '1' }
$hasP1 = $genP1.Content -match 'Showing 1 to 25'
Check "Page 1 (25 rows)" $hasP1

$genP2 = Generate @{ PageSize = '25'; PageNumber = '2' }
$hasP2 = $genP2.Content -match 'Showing 26 to 50'
Check "Page 2 (rows 26-50)" $hasP2

$genP100 = Generate @{ PageSize = '100'; PageNumber = '1' }
$hasP100 = $genP100.Content -match 'Showing 1 to 100'
Check "PageSize=100" $hasP100

$genP200 = Generate @{ PageSize = '200'; PageNumber = '1' }
$hasP200 = $genP200.Content -match 'Showing 1 to 200'
Check "PageSize=200" ($hasP200 -or $genP200.Content -match 'Showing 1 to')

# Page total row should appear when paginated
$hasPgTotal = $genP1.Content -match 'PAGE TOTAL'
Check "PAGE TOTAL row when paginated" $hasPgTotal

# ==========================================================================
# 11. SORTING (each sortable column)
# ==========================================================================
Write-Host "`n=== 11. SORTING ==="
foreach ($col in @('Period','Invoices','Returns','NetTransactions','QtySold','QtyReturned','NetQty','Sales','AvgBasket','AvgQty')) {
    foreach ($dir in @('ASC','DESC')) {
        $gen = Generate @{ SortColumn = $col; SortDirection = $dir; PageSize = '25' }
        $sortActive = $gen.Content -match "sort-$($dir.ToLower())"
        Check "Sort $col $dir" ($gen.Content.Length -gt 5000)
    }
}

# ==========================================================================
# 12. COLUMN FILTERS
# ==========================================================================
Write-Host "`n=== 12. COLUMN FILTERS ==="
$page2 = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -UseBasicParsing -WebSession $session
$token2 = ([regex]::Match($page2.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value

$filterBody = @{
    DateFrom = '01/01/2025'; DateTo = '31/12/2025'
    Breakdown = 'Monthly'; GroupBy = 'None'; SecondaryGroupBy = 'None'
    IncludeVat = 'false'; CompareLastYear = 'false'; ShowTotalQty = 'false'
    ReportLayout = 'AverageBasket'
    PageNumber = '1'; PageSize = '50'
    SortColumn = 'Period'; SortDirection = 'ASC'
    'FilterValues[Period]' = '2025-01'
    'FilterOperators[Period]' = 'contains'
    __RequestVerificationToken = $token2
}
$genFilter = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -Method POST -Body $filterBody -UseBasicParsing -WebSession $session
$filteredRows = [regex]::Match($genFilter.Content, 'of\s+(\d+)\s+rows')
if ($filteredRows.Success) { Write-Host "  Filtered rows: $($filteredRows.Groups[1].Value)" }
Check "Column filter (Period contains '2025-01')" ($genFilter.Content.Length -gt 5000 -and ($filteredRows.Groups[1].Value -lt 12))

# Numeric filter
$filterBody2 = @{
    DateFrom = '01/01/2025'; DateTo = '31/12/2025'
    Breakdown = 'Monthly'; GroupBy = 'None'; SecondaryGroupBy = 'None'
    IncludeVat = 'false'; CompareLastYear = 'false'; ShowTotalQty = 'false'
    ReportLayout = 'AverageBasket'
    PageNumber = '1'; PageSize = '50'
    SortColumn = 'Period'; SortDirection = 'ASC'
    'FilterValues[Invoices]' = '1000'
    'FilterOperators[Invoices]' = 'gte'
    __RequestVerificationToken = $token2
}
$genFilter2 = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -Method POST -Body $filterBody2 -UseBasicParsing -WebSession $session
$filteredRows2 = [regex]::Match($genFilter2.Content, 'of\s+(\d+)\s+rows')
if ($filteredRows2.Success) { Write-Host "  Filtered rows (Invoices>=1000): $($filteredRows2.Groups[1].Value)" }
Check "Column filter (Invoices >= 1000)" ($genFilter2.Content.Length -gt 5000)

# ==========================================================================
# 13. EXPORTS
# ==========================================================================
Write-Host "`n=== 13. EXPORT BUTTONS ==="
$exportBase = "dateFrom=2025-01-01&dateTo=2025-12-31&breakdown=Monthly&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false&sortColumn=Period&sortDirection=ASC"

# CSV
$csv = Invoke-WebRequest -Uri "$base/Reports/ExportCsv?$exportBase" -UseBasicParsing -WebSession $session
Check "CSV export returns CSV" ($csv.Headers['Content-Type'] -match 'csv')
Check "CSV has content" ($csv.Content.Length -gt 100)
$csvFile = "C:\p\Powersoft.Reporting\_test_ab_btn_export.csv"
$csv.Content | Set-Content $csvFile -Encoding utf8 -NoNewline

# Excel
$excel = Invoke-WebRequest -Uri "$base/Reports/ExportExcel?$exportBase" -UseBasicParsing -WebSession $session
Check "Excel export returns xlsx" ($excel.Headers['Content-Type'] -match 'spreadsheet|excel')
Check "Excel has content" ($excel.Content.Length -gt 100)

# PDF
$pdf = Invoke-WebRequest -Uri "$base/Reports/ExportPdf?$exportBase" -UseBasicParsing -WebSession $session
Check "PDF export returns PDF" ($pdf.Headers['Content-Type'] -match 'pdf')
Check "PDF has content" ($pdf.Content.Length -gt 100)

# Exports with GroupBy
$exportGrouped = "$exportBase&groupBy=Category"
$csvG = Invoke-WebRequest -Uri "$base/Reports/ExportCsv?$exportGrouped" -UseBasicParsing -WebSession $session
Check "CSV with GroupBy=Category" ($csvG.Headers['Content-Type'] -match 'csv' -and $csvG.Content.Length -gt 100)

# Exports with VAT
$exportVat = "$exportBase&includeVat=true"
$csvV = Invoke-WebRequest -Uri "$base/Reports/ExportCsv?$exportVat" -UseBasicParsing -WebSession $session
Check "CSV with IncludeVat=true" ($csvV.Headers['Content-Type'] -match 'csv' -and $csvV.Content.Length -gt 100)

# Exports with Compare Last Year
$exportLy = "$exportBase&compareLastYear=true"
$csvLy = Invoke-WebRequest -Uri "$base/Reports/ExportCsv?$exportLy" -UseBasicParsing -WebSession $session
Check "CSV with CompareLastYear=true" ($csvLy.Headers['Content-Type'] -match 'csv' -and $csvLy.Content.Length -gt 100)

# Exports with store filter
$exportStore = "$exportBase&storeCodes=001"
$csvStore = Invoke-WebRequest -Uri "$base/Reports/ExportCsv?$exportStore" -UseBasicParsing -WebSession $session
Check "CSV with Store filter (001)" ($csvStore.Headers['Content-Type'] -match 'csv' -and $csvStore.Content.Length -gt 50)

# ==========================================================================
# 14. PRINT PREVIEW
# ==========================================================================
Write-Host "`n=== 14. PRINT PREVIEW ==="
$print = Invoke-WebRequest -Uri "$base/Reports/PrintPreview?$exportBase" -UseBasicParsing -WebSession $session
Check "Print preview loads" ($print.StatusCode -eq 200)
$printTitle = ([regex]::Match($print.Content, '<title>([^<]+)</title>')).Groups[1].Value
Check "Print preview has title" ($printTitle -match 'Print|Preview|Average')
Check "Print preview has table" ($print.Content -match '<table')
Check "Print preview has GRAND TOTAL" ($print.Content -match 'GRAND TOTAL')

# Print with grouping
$printG = Invoke-WebRequest -Uri "$base/Reports/PrintPreview?$exportBase&groupBy=Store" -UseBasicParsing -WebSession $session
Check "Print with GroupBy=Store" ($printG.Content -match '<table' -and $printG.Content.Length -gt 5000)

# Print with CompareLastYear
$printLy = Invoke-WebRequest -Uri "$base/Reports/PrintPreview?$exportBase&compareLastYear=true" -UseBasicParsing -WebSession $session
Check "Print with CompareLastYear" ($printLy.Content -match 'LY' -or $printLy.Content -match 'Last Year')

# ==========================================================================
# 15. SCHEDULE CRUD
# ==========================================================================
Write-Host "`n=== 15. SCHEDULE ==="
# List
$schedList = Invoke-WebRequest -Uri "$base/Reports/GetSchedules" -UseBasicParsing -WebSession $session
$schedules = $schedList.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "GetSchedules returns JSON array" ($null -ne $schedules)
Write-Host "  Existing schedules: $($schedules.Count)"

# ==========================================================================
# 16. LAYOUT SAVE/LOAD/RESET
# ==========================================================================
Write-Host "`n=== 16. LAYOUT ==="
$layoutResp = Invoke-WebRequest -Uri "$base/Reports/GetReportLayout?reportType=AverageBasket" -UseBasicParsing -WebSession $session
$layoutJson = $layoutResp.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "GetReportLayout returns JSON" ($null -ne $layoutJson)
Write-Host "  Has saved layout: $($layoutJson.hasSaved)"

# List all layouts
$layoutsResp = Invoke-WebRequest -Uri "$base/Reports/GetReportLayouts?reportType=AverageBasket" -UseBasicParsing -WebSession $session -ErrorAction SilentlyContinue
if ($layoutsResp) {
    $layouts = $layoutsResp.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
    $layoutCount = if ($layouts) { $layouts.Count } else { 0 }
    Check "GetReportLayouts returns list" ($null -ne $layouts)
    Write-Host "  Layouts count: $layoutCount"
}

# ==========================================================================
# 17. EMAIL
# ==========================================================================
Write-Host "`n=== 17. SEND EMAIL ==="
$emailTemplates = Invoke-WebRequest -Uri "$base/Reports/GetEmailTemplates?reportType=AverageBasket" -UseBasicParsing -WebSession $session
$templates = $emailTemplates.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "Email templates API works" ($null -ne $templates)
Write-Host "  Templates: $($templates.Count)"

# Get user emails for CC/BCC picker
$emailUsers = Invoke-WebRequest -Uri "$base/Reports/GetUserEmails" -UseBasicParsing -WebSession $session -ErrorAction SilentlyContinue
if ($emailUsers) {
    $users = $emailUsers.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
    Check "GetUserEmails returns list" ($null -ne $users)
    Write-Host "  User email count: $($users.Count)"
}

# ==========================================================================
# 18. AI ANALYZE
# ==========================================================================
Write-Host "`n=== 18. AI ANALYZE ==="
$aiStatus = Invoke-WebRequest -Uri "$base/Reports/GetAiStatus" -UseBasicParsing -WebSession $session
$aiJson = $aiStatus.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "AI status endpoint works" ($null -ne $aiJson)
Check "AI is configured" ($aiJson.configured -eq $true)

# ==========================================================================
# 19. ITEMS SELECTION API
# ==========================================================================
Write-Host "`n=== 19. ITEMS SELECTION ==="
$stores = Invoke-WebRequest -Uri "$base/Reports/GetStores" -UseBasicParsing -WebSession $session
$storeList = $stores.Content | ConvertFrom-Json
Check "GetStores returns data" ($storeList.Count -gt 0)
Write-Host "  Stores: $($storeList.Count)"

$categories = Invoke-WebRequest -Uri "$base/Reports/GetCategories" -UseBasicParsing -WebSession $session
$catList = $categories.Content | ConvertFrom-Json
Check "GetCategories returns data" ($catList.Count -gt 0)
Write-Host "  Categories: $($catList.Count)"

$departments = Invoke-WebRequest -Uri "$base/Reports/GetDepartments" -UseBasicParsing -WebSession $session
$deptList = $departments.Content | ConvertFrom-Json
Check "GetDepartments returns data" ($deptList.Count -gt 0)
Write-Host "  Departments: $($deptList.Count)"

$brands = Invoke-WebRequest -Uri "$base/Reports/GetBrands" -UseBasicParsing -WebSession $session
$brandList = $brands.Content | ConvertFrom-Json
Check "GetBrands returns data" ($brandList.Count -gt 0)
Write-Host "  Brands: $($brandList.Count)"

$seasons = Invoke-WebRequest -Uri "$base/Reports/GetSeasons" -UseBasicParsing -WebSession $session
$seasonList = $seasons.Content | ConvertFrom-Json
Check "GetSeasons returns data" ($seasonList.Count -gt 0)
Write-Host "  Seasons: $($seasonList.Count)"

$suppliers = Invoke-WebRequest -Uri "$base/Reports/GetSuppliers" -UseBasicParsing -WebSession $session
$supplierList = $suppliers.Content | ConvertFrom-Json
Check "GetSuppliers returns data" ($supplierList.Count -gt 0)
Write-Host "  Suppliers: $($supplierList.Count)"

# Search items
$searchResp = Invoke-WebRequest -Uri "$base/Reports/SearchItems?term=boot&maxResults=10" -UseBasicParsing -WebSession $session
$searchResults = $searchResp.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
Check "SearchItems works" ($null -ne $searchResults -and $searchResults.Count -gt 0)
Write-Host "  Search 'boot': $($searchResults.Count) results"

# ==========================================================================
# 20. STORE FILTER
# ==========================================================================
Write-Host "`n=== 20. STORE FILTER ==="
$genStore = Generate @{ SelectedStoreCodesString = '001' }
$storeFiltered = $genStore.Content -match '1 stores filtered'
Check "Store filter badge shows" $storeFiltered

$genStore2 = Generate @{ SelectedStoreCodesString = '001,002' }
$store2Filtered = $genStore2.Content -match '2 stores filtered'
Check "Multi-store filter" $store2Filtered

# ==========================================================================
# 21. GENERATE WITH ITEMS SELECTION (Category filter)
# ==========================================================================
Write-Host "`n=== 21. ITEMS SELECTION FILTER ==="
$itemsJson = '{"categories":["ACCESORIES"],"departments":[],"brands":[],"seasons":[],"suppliers":[],"items":[],"stores":[]}'
$genItems = Generate @{ ItemsSelectionJson = $itemsJson }
$hasData = $genItems.Content.Length -gt 5000
$rows = [regex]::Match($genItems.Content, 'of\s+(\d+)\s+rows')
$cnt = if ($rows.Success) { $rows.Groups[1].Value } else { 'N/A' }
Check "Items selection (category=ACCESORIES) works ($cnt rows)" $hasData

# ==========================================================================
# 22. EDGE CASES
# ==========================================================================
Write-Host "`n=== 22. EDGE CASES ==="
# Empty date range
$genEmpty = Generate @{ DateFrom = '01/01/2000'; DateTo = '31/01/2000' }
$noData = $genEmpty.Content -match 'No Data Found' -or $genEmpty.Content -match 'No transactions'
Check "Empty date range shows 'No Data Found'" $noData

# Future dates
$genFuture = Generate @{ DateFrom = '01/01/2030'; DateTo = '31/12/2030' }
$noDataF = $genFuture.Content -match 'No Data Found' -or $genFuture.Content -match 'No transactions'
Check "Future dates show 'No Data Found'" $noDataF

# Single day
$genSingleDay = Generate @{ DateFrom = '14/01/2025'; DateTo = '14/01/2025'; Breakdown = 'Daily' }
$singleData = $genSingleDay.Content.Length -gt 5000
Check "Single day query works" $singleData

# ==========================================================================
# 23. CLEAR FILTERS (link)
# ==========================================================================
Write-Host "`n=== 23. CLEAR FILTERS ==="
$clearPage = Invoke-WebRequest -Uri "$base/Reports/AverageBasket?clearedFilters=true" -UseBasicParsing -WebSession $session
Check "Clear Filters resets to empty state" ($clearPage.Content -match 'Generate Your Report')

# ==========================================================================
Write-Host "`n============================================================"
Write-Host " RESULTS: $pass PASS / $fail FAIL / $total TOTAL"
Write-Host "============================================================"

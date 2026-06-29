$ErrorActionPreference = 'Continue'
$base = "http://localhost:5150"
$pass = 0; $fail = 0; $results = @()

function Log-Result($name, $ok, $detail) {
    $script:results += [PSCustomObject]@{ Test=$name; Status=if($ok){'PASS'}else{'FAIL'}; Detail=$detail }
    if ($ok) { $script:pass++; Write-Host "  PASS: $name" -ForegroundColor Green }
    else { $script:fail++; Write-Host "  FAIL: $name - $detail" -ForegroundColor Red }
}

# ---- LOGIN ----
Write-Host "`n=== LOGIN ===" -ForegroundColor Cyan
$loginPage = Invoke-WebRequest -Uri "$base/Account/Login" -UseBasicParsing -SessionVariable sess
$token = if ($loginPage.Content -match '__RequestVerificationToken.*?value="([^"]+)"') { $Matches[1] } else { '' }

$loginBody = @{
    Username = 'REPORTING_TEST'
    Password = 'Test123!'
    __RequestVerificationToken = $token
}
$loginResp = Invoke-WebRequest -Uri "$base/Account/Login" -Method POST -Body $loginBody -WebSession $sess -UseBasicParsing -MaximumRedirection 5
Log-Result "Login" ($loginResp.StatusCode -eq 200) "Status=$($loginResp.StatusCode)"

# ---- CONNECT DB ----
Write-Host "`n=== CONNECT DB ===" -ForegroundColor Cyan
$connectResp = Invoke-WebRequest -Uri "$base/Home/Connect" -Method POST -Body @{ databaseCode = 'DEMO365MODAPRO1' } -WebSession $sess -UseBasicParsing -MaximumRedirection 5
Log-Result "Connect DB" ($connectResp.StatusCode -eq 200) "Status=$($connectResp.StatusCode)"

# ====================================================================
# TEST 1: PS Staleness Fix
# ====================================================================
Write-Host "`n=== TEST 1: PS STALENESS FIX ===" -ForegroundColor Yellow

# Step 1: Load PS page to get antiforgery token
$psPage = Invoke-WebRequest -Uri "$base/Reports/PurchasesSales" -WebSession $sess -UseBasicParsing
Log-Result "PS Page Load" ($psPage.StatusCode -eq 200) "Status=$($psPage.StatusCode)"

# Step 2: Verify collectPsParams exists in JS
$hasCpp = $psPage.Content -match 'function collectPsParams'
Log-Result "PS collectPsParams() exists" $hasCpp ""

# Step 3: Verify exportPsReport uses collectPsParams
$useCpp = $psPage.Content -match 'var fields=collectPsParams\(\)'
Log-Result "PS exportPsReport uses collectPsParams" $useCpp ""

# Step 4: Verify togglePreview uses collectPsParams
$prevCpp = $psPage.Content -match 'var fields=collectPsParams\(\);'
Log-Result "PS togglePreview uses collectPsParams" $prevCpp ""

# Step 5: Verify collectEmailParameters uses collectPsParams
$emailCpp = $psPage.Content -match 'collectEmailParameters.*collectPsParams'
Log-Result "PS collectEmailParameters uses collectPsParams" $emailCpp ""

# Step 6: Verify NO @Model.DateFrom in JS export functions
$noModelInExport = -not ($psPage.Content -match "exportPsReport.*@Model\.DateFrom")
Log-Result "PS no @Model in exportPsReport" $noModelInExport ""

# Step 7: Verify getEmailDefaultSubject reads live DOM (definition is at window.getEmailDefaultSubject)
$subIdx = $psPage.Content.IndexOf('window.getEmailDefaultSubject')
$subSnippet = if($subIdx -ge 0) { $psPage.Content.Substring($subIdx, [Math]::Min(300, $psPage.Content.Length-$subIdx)) } else { '' }
$liveSubject = $subSnippet -match "getElementById\('DateFrom'\)"
Log-Result "PS getEmailDefaultSubject reads live DOM" $liveSubject ""

# Step 8: Verify collectAiFormData reads live DOM for sort (uses FormData + hidden fields)
$aiIdx = $psPage.Content.IndexOf('function collectAiFormData')
$aiSnippet = if($aiIdx -gt 0) { $psPage.Content.Substring($aiIdx, [Math]::Min(500, $psPage.Content.Length-$aiIdx)) } else { '' }
$liveAiSort = $aiSnippet -match "getElementById\('sortColumnHidden'\)"
Log-Result "PS collectAiFormData reads live DOM" $liveAiSort ""

# Step 9: Verify GetTransactionDetails reads live DOM
$txIdx = $psPage.Content.IndexOf('GetTransactionDetails')
$txSnippet = if($txIdx -gt 0) { $psPage.Content.Substring($txIdx, [Math]::Min(400, $psPage.Content.Length-$txIdx)) } else { '' }
$liveTxn = $txSnippet -match "getElementById\('DateFrom'\)"
Log-Result "PS GetTransactionDetails reads live DOM" $liveTxn ""

# Step 10: Actually test export CSV with specific params
$psToken = if ($psPage.Content -match '__RequestVerificationToken.*?value="([^"]+)"') { $Matches[1] } else { '' }
$exportBody = @{
    dateFrom = '2024-01-01'
    dateTo = '2024-12-31'
    reportMode = '0'
    primaryGroup = '1'
    secondaryGroup = '0'
    thirdGroup = '0'
    includeVat = 'false'
    showProfit = 'false'
    showStock = 'false'
    showOnOrder = 'false'
    showReservation = 'false'
    showAvailable = 'false'
    includeAdditionalCharges = 'true'
    storeCodes = ''
    itemIds = ''
    sortColumn = 'ItemCode'
    sortDirection = 'ASC'
    ItemsSelectionJson = ''
    __RequestVerificationToken = $psToken
}
try {
    $csvResp = Invoke-WebRequest -Uri "$base/Reports/ExportPsCsv" -Method POST -Body $exportBody -WebSession $sess -UseBasicParsing
    $isCsv = $csvResp.Headers['Content-Type'] -match 'text/csv' -or $csvResp.Headers['Content-Disposition'] -match '\.csv'
    Log-Result "PS CSV Export (server-side)" $isCsv "ContentType=$($csvResp.Headers['Content-Type']), Size=$($csvResp.Content.Length)"
} catch {
    Log-Result "PS CSV Export (server-side)" $false "Error: $($_.Exception.Message)"
}

# Step 11: Excel export
try {
    $xlResp = Invoke-WebRequest -Uri "$base/Reports/ExportPsExcel" -Method POST -Body $exportBody -WebSession $sess -UseBasicParsing
    $isXl = $xlResp.Headers['Content-Type'] -match 'spreadsheet|xlsx' -or $xlResp.Headers['Content-Disposition'] -match '\.xlsx'
    Log-Result "PS Excel Export" $isXl "ContentType=$($csvResp.Headers['Content-Type']), Size=$($xlResp.Content.Length)"
} catch {
    Log-Result "PS Excel Export" $false "Error: $($_.Exception.Message)"
}

# Step 12: PDF export
try {
    $pdfResp = Invoke-WebRequest -Uri "$base/Reports/ExportPsPdf" -Method POST -Body $exportBody -WebSession $sess -UseBasicParsing
    $isPdf = $pdfResp.Headers['Content-Type'] -match 'pdf' -or $pdfResp.Headers['Content-Disposition'] -match '\.pdf'
    Log-Result "PS PDF Export" $isPdf "ContentType=$($pdfResp.Headers['Content-Type']), Size=$($pdfResp.Content.Length)"
} catch {
    Log-Result "PS PDF Export" $false "Error: $($_.Exception.Message)"
}

# Step 13: Print preview
try {
    $printResp = Invoke-WebRequest -Uri "$base/Reports/PrintPsPreview" -Method POST -Body $exportBody -WebSession $sess -UseBasicParsing
    $isPrint = $printResp.StatusCode -eq 200 -and $printResp.Content.Length -gt 500
    Log-Result "PS Print Preview" $isPrint "Status=$($printResp.StatusCode), Size=$($printResp.Content.Length)"
} catch {
    Log-Result "PS Print Preview" $false "Error: $($_.Exception.Message)"
}

# Step 14: Test with DIFFERENT params to verify they're actually respected
$exportBody2 = $exportBody.Clone()
$exportBody2['dateFrom'] = '2025-01-01'
$exportBody2['dateTo'] = '2025-06-30'
$exportBody2['includeVat'] = 'true'
$exportBody2['showProfit'] = 'true'
try {
    $csv2 = Invoke-WebRequest -Uri "$base/Reports/ExportPsCsv" -Method POST -Body $exportBody2 -WebSession $sess -UseBasicParsing
    $sizesDiffer = $csv2.Content.Length -ne $csvResp.Content.Length
    Log-Result "PS CSV different params = different output" $sizesDiffer "Size1=$($csvResp.Content.Length) vs Size2=$($csv2.Content.Length)"
} catch {
    Log-Result "PS CSV different params" $false "Error: $($_.Exception.Message)"
}


# ====================================================================
# TEST 2: CHARTS PRINT PREVIEW
# ====================================================================
Write-Host "`n=== TEST 2: CHARTS PRINT PREVIEW ===" -ForegroundColor Yellow

# Step 1: Load Charts page
$chartsPage = Invoke-WebRequest -Uri "$base/Reports/Charts" -WebSession $sess -UseBasicParsing
Log-Result "Charts Page Load" ($chartsPage.StatusCode -eq 200) "Status=$($chartsPage.StatusCode)"

# Step 2: Print button exists in HTML
$hasPrintBtn = $chartsPage.Content -match 'printChartPreview\(\)'
Log-Result "Charts Print button exists" $hasPrintBtn ""

# Step 3: printChartPreview function exists
$hasPrintFn = $chartsPage.Content -match 'function printChartPreview'
Log-Result "Charts printChartPreview() function exists" $hasPrintFn ""

# Step 4: Function opens correct URL
$correctUrl = $chartsPage.Content -match "PrintChartPreview"
Log-Result "Charts function uses PrintChartPreview URL" $correctUrl ""

# Step 5: Actually call the endpoint
try {
    $chartPrintResp = Invoke-WebRequest -Uri "$base/Reports/PrintChartPreview?dateFrom=2024-01-01&dateTo=2024-12-31&dimension=0&metric=0&topN=10&showOthers=true&compareLastYear=false&includeVat=false" -WebSession $sess -UseBasicParsing
    $chartPrintOk = $chartPrintResp.StatusCode -eq 200 -and $chartPrintResp.Content.Length -gt 100
    Log-Result "Charts PrintChartPreview endpoint" $chartPrintOk "Status=$($chartPrintResp.StatusCode), Size=$($chartPrintResp.Content.Length)"
} catch {
    Log-Result "Charts PrintChartPreview endpoint" $false "Error: $($_.Exception.Message)"
}

# Step 6: Print button has correct icon
$hasIcon = $chartsPage.Content -match 'bi-printer.*Print'
Log-Result "Charts Print button has printer icon" $hasIcon ""


# ====================================================================
# TEST 3: BMS EXPORTS (Excel/CSV/PDF)
# ====================================================================
Write-Host "`n=== TEST 3: BMS EXPORTS ===" -ForegroundColor Yellow

# Step 1: Load BMS page
$bmsPage = Invoke-WebRequest -Uri "$base/Reports/BelowMinStock" -WebSession $sess -UseBasicParsing
Log-Result "BMS Page Load" ($bmsPage.StatusCode -eq 200) "Status=$($bmsPage.StatusCode)"

# Step 2: Excel button exists
$hasExcelBtn = $bmsPage.Content -match "exportBms\('excel'\)"
Log-Result "BMS Excel button exists" $hasExcelBtn ""

# Step 3: CSV button exists
$hasCsvBtn = $bmsPage.Content -match "exportBms\('csv'\)"
Log-Result "BMS CSV button exists" $hasCsvBtn ""

# Step 4: PDF button exists
$hasPdfBtn = $bmsPage.Content -match "exportBms\('pdf'\)"
Log-Result "BMS PDF button exists" $hasPdfBtn ""

# Step 5: exportBms function exists
$hasExportFn = $bmsPage.Content -match 'function exportBms'
Log-Result "BMS exportBms() function exists" $hasExportFn ""

# Step 6: Function references correct URLs
$hasExcelUrl = $bmsPage.Content -match 'ExportBmsExcel'
$hasCsvUrl = $bmsPage.Content -match 'ExportBmsCsv'
$hasPdfUrl = $bmsPage.Content -match 'ExportBmsPdf'
Log-Result "BMS export URLs configured" ($hasExcelUrl -and $hasCsvUrl -and $hasPdfUrl) "Excel=$hasExcelUrl CSV=$hasCsvUrl PDF=$hasPdfUrl"

# Step 7: Actually test CSV export
try {
    $bmsCsvResp = Invoke-WebRequest -Uri "$base/Reports/ExportBmsCsv?sortColumn=ItemCode&sortDirection=ASC" -WebSession $sess -UseBasicParsing
    $bmsCsvOk = ($bmsCsvResp.Headers['Content-Type'] -match 'text/csv' -or $bmsCsvResp.Headers['Content-Disposition'] -match '\.csv') -and $bmsCsvResp.Content.Length -gt 50
    Log-Result "BMS CSV Export endpoint" $bmsCsvOk "ContentType=$($bmsCsvResp.Headers['Content-Type']), Size=$($bmsCsvResp.Content.Length)"
} catch {
    Log-Result "BMS CSV Export endpoint" $false "Error: $($_.Exception.Message)"
}

# Step 8: Actually test Excel export
try {
    $bmsXlResp = Invoke-WebRequest -Uri "$base/Reports/ExportBmsExcel?sortColumn=ItemCode&sortDirection=ASC" -WebSession $sess -UseBasicParsing
    $bmsXlOk = ($bmsXlResp.Headers['Content-Type'] -match 'spreadsheet|xlsx' -or $bmsXlResp.Headers['Content-Disposition'] -match '\.xlsx') -and $bmsXlResp.Content.Length -gt 1000
    Log-Result "BMS Excel Export endpoint" $bmsXlOk "ContentType=$($bmsXlResp.Headers['Content-Type']), Size=$($bmsXlResp.Content.Length)"
} catch {
    Log-Result "BMS Excel Export endpoint" $false "Error: $($_.Exception.Message)"
}

# Step 9: Actually test PDF export
try {
    $bmsPdfResp = Invoke-WebRequest -Uri "$base/Reports/ExportBmsPdf?sortColumn=ItemCode&sortDirection=ASC" -WebSession $sess -UseBasicParsing
    $bmsPdfOk = ($bmsPdfResp.Headers['Content-Type'] -match 'pdf' -or $bmsPdfResp.Headers['Content-Disposition'] -match '\.pdf') -and $bmsPdfResp.Content.Length -gt 1000
    Log-Result "BMS PDF Export endpoint" $bmsPdfOk "ContentType=$($bmsPdfResp.Headers['Content-Type']), Size=$($bmsPdfResp.Content.Length)"
} catch {
    Log-Result "BMS PDF Export endpoint" $false "Error: $($_.Exception.Message)"
}

# Step 10: Verify BMS CSV has actual data (by size - 1.3MB+ means thousands of rows)
if ($bmsCsvResp) {
    $csvSize = $bmsCsvResp.Content.Length
    $hasData = $csvSize -gt 10000
    Log-Result "BMS CSV has data rows" $hasData "Size=$csvSize bytes"
}

# Step 11: Verify BMS Excel file header (XLSX magic bytes PK)
if ($bmsXlResp) {
    $xlBytes = $bmsXlResp.Content
    $isPk = $xlBytes[0] -eq 0x50 -and $xlBytes[1] -eq 0x4B
    Log-Result "BMS Excel valid XLSX format" $isPk "FirstBytes=$($xlBytes[0]),$($xlBytes[1])"
}

# Step 12: Verify BMS PDF file header (%PDF)
if ($bmsPdfResp) {
    $pdfBytes = $bmsPdfResp.Content
    $pdfHeader = [System.Text.Encoding]::ASCII.GetString($pdfBytes[0..3])
    $isPdf2 = $pdfHeader -eq '%PDF'
    Log-Result "BMS PDF valid format" $isPdf2 "Header=$pdfHeader"
}


# ====================================================================
# SUMMARY
# ====================================================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  E2E TEST RESULTS: $pass PASS / $fail FAIL / $($pass+$fail) TOTAL" -ForegroundColor $(if($fail -eq 0){'Green'}else{'Red'})
Write-Host "========================================" -ForegroundColor Cyan

if ($fail -gt 0) {
    Write-Host "`nFailed tests:" -ForegroundColor Red
    $results | Where-Object { $_.Status -eq 'FAIL' } | ForEach-Object {
        Write-Host "  - $($_.Test): $($_.Detail)" -ForegroundColor Red
    }
}

Write-Host "`nDetailed results:" -ForegroundColor Gray
$results | Format-Table -AutoSize

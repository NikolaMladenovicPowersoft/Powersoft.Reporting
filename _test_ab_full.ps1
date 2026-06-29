$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5150'
$cookiePath = 'C:\p\Powersoft.Reporting\_test_cookies.json'
$results = @()

function Log($msg, $status) {
    $icon = switch ($status) { 'OK' { '[PASS]' }; 'FAIL' { '[FAIL]' }; 'WARN' { '[WARN]' }; default { '[INFO]' } }
    Write-Host "$icon $msg"
    $script:results += @{ Test = $msg; Status = $status }
}

# Reload session
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$cookies = Get-Content $cookiePath -Raw | ConvertFrom-Json
foreach ($c in $cookies) {
    $cookie = New-Object System.Net.Cookie($c.Name, $c.Value, $c.Path, $c.Domain)
    $session.Cookies.Add($(New-Object System.Uri($base)), $cookie)
}

Write-Host "=========================================="
Write-Host " AVERAGE BASKET - FULL FUNCTIONALITY TEST"
Write-Host "=========================================="
Write-Host ""

# =============================================
# 1. PAGE LOAD
# =============================================
Write-Host "=== 1. PAGE LOAD ==="
$abPage = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -UseBasicParsing -WebSession $session
$abTitle = ([regex]::Match($abPage.Content, '<title>([^<]+)</title>')).Groups[1].Value

if ($abTitle -match 'Average Basket') {
    Log "Page load - correct title" 'OK'
} else {
    Log "Page load - wrong title: $abTitle" 'FAIL'
}

# Check filter controls
$filterChecks = @{
    'DateFrom input' = 'id="dateFrom"'
    'DateTo input' = 'id="dateTo"'
    'Breakdown dropdown' = 'id="breakdown"'
    'GroupBy dropdown' = 'id="groupBy"'
    'SecondaryGroupBy dropdown' = 'id="secondaryGroupBy"'
    'IncludeVat checkbox' = 'includeVat'
    'CompareLastYear checkbox' = 'compareLastYear'
    'Sort Column' = 'sortColumn'
    'Sort Direction' = 'sortDirection'
    'Generate button' = 'generateReport'
    'Print Preview' = 'togglePreview\(\)|PrintPreview'
    'Schedule partial' = '_Schedule|scheduleModal'
    'Email partial' = '_SendEmail|emailModal'
    'Layout partial' = '_SaveLayout|layoutModal'
    'AI partial' = '_AiAnalyze|aiModal|openAiAnalysis'
    'Items Selection partial' = '_ItemsSelection|itemsSelection'
    'Date preset' = 'datePreset|dateRangeType|applyDateRange'
    'Export Excel JS' = 'ExportExcel|exportExcel|buildReportUrl'
    'Export CSV JS' = 'ExportCsv|exportCsv'
    'Export PDF JS' = 'ExportPdf|exportPdf'
}

Write-Host ""
foreach ($check in $filterChecks.GetEnumerator()) {
    $found = $abPage.Content -match $check.Value
    Log "UI: $($check.Key)" $(if ($found) { 'OK' } else { 'FAIL' })
}

# Check Breakdown options
$breakdownOptions = [regex]::Matches($abPage.Content, '<option[^>]*value="([^"]*)"[^>]*>([^<]+)</option>')
$breakdownValues = @()
$inBreakdown = $false
foreach ($line in ($abPage.Content -split "`n")) {
    if ($line -match 'id="breakdown"') { $inBreakdown = $true; continue }
    if ($inBreakdown -and $line -match '</select>') { $inBreakdown = $false; break }
    if ($inBreakdown -and $line -match 'value="([^"]*)"[^>]*>([^<]+)') {
        $breakdownValues += "$($Matches[1])=$($Matches[2])"
    }
}
Write-Host "`nBreakdown options: $($breakdownValues -join ', ')"

# Check GroupBy options
$groupValues = @()
$inGroup = $false
foreach ($line in ($abPage.Content -split "`n")) {
    if ($line -match 'id="groupBy"') { $inGroup = $true; continue }
    if ($inGroup -and $line -match '</select>') { $inGroup = $false; break }
    if ($inGroup -and $line -match 'value="([^"]*)"[^>]*>([^<]+)') {
        $groupValues += "$($Matches[1])=$($Matches[2])"
    }
}
Write-Host "GroupBy options: $($groupValues -join ', ')"

# Extract default date values
$dateFromDefault = ([regex]::Match($abPage.Content, 'id="dateFrom"[^>]*value="([^"]*)"')).Groups[1].Value
$dateToDefault = ([regex]::Match($abPage.Content, 'id="dateTo"[^>]*value="([^"]*)"')).Groups[1].Value
if ([string]::IsNullOrEmpty($dateFromDefault)) {
    $dateFromDefault = ([regex]::Match($abPage.Content, 'value="([^"]*)"[^>]*id="dateFrom"')).Groups[1].Value
}
if ([string]::IsNullOrEmpty($dateToDefault)) {
    $dateToDefault = ([regex]::Match($abPage.Content, 'value="([^"]*)"[^>]*id="dateTo"')).Groups[1].Value
}
Write-Host "Default dates: $dateFromDefault to $dateToDefault"

# =============================================
# 2. GENERATE REPORT (POST)
# =============================================
Write-Host "`n=== 2. GENERATE REPORT ==="

# Get antiforgery token from AB page
$abToken = ([regex]::Match($abPage.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
if ([string]::IsNullOrEmpty($abToken)) {
    $abToken = ([regex]::Match($abPage.Content, 'value="([^"]+)"[^>]*name="__RequestVerificationToken"')).Groups[1].Value
}

# Test with default parameters (POST form)
$formData = @{
    DateFrom = '01/01/2025'
    DateTo = '31/12/2025'
    Breakdown = 'Daily'
    GroupBy = 'None'
    SecondaryGroupBy = 'None'
    IncludeVat = 'false'
    CompareLastYear = 'false'
    PageNumber = '1'
    PageSize = '50'
    __RequestVerificationToken = $abToken
}

$genResp = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -Method POST -Body $formData -UseBasicParsing -WebSession $session
$genTitle = ([regex]::Match($genResp.Content, '<title>([^<]+)</title>')).Groups[1].Value
Write-Host "Generate response title: $genTitle"
Write-Host "Generate response size: $($genResp.Content.Length)"

# Check for result data
$hasResults = $genResp.Content -match 'reportData|tbody|data-row|resultsTable|resultCount'
$rowCount = ([regex]::Matches($genResp.Content, '<tr[^>]*class="[^"]*data-row')).Count
$totalRowsMatch = [regex]::Match($genResp.Content, 'totalRows["\s:=]+(\d+)|Total.*?(\d+)\s*row|Showing.*?of\s*(\d+)')

Log "Generate returns data" $(if ($hasResults) { 'OK' } else { 'FAIL' })
Write-Host "Visible rows: $rowCount"
Write-Host "Total rows match: $($totalRowsMatch.Value)"

# Save generated page
$genResp.Content | Out-File 'C:\p\Powersoft.Reporting\_test_ab_generated.html' -Encoding utf8
Write-Host "Generated page saved"

# =============================================
# 3. EXPORT EXCEL
# =============================================
Write-Host "`n=== 3. EXPORT EXCEL ==="
try {
    $excelResp = Invoke-WebRequest -Uri "$base/Reports/ExportExcel?dateFrom=01/01/2025&dateTo=31/12/2025&breakdown=Daily&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false" -UseBasicParsing -WebSession $session
    $contentType = $excelResp.Headers['Content-Type']
    $contentDisp = $excelResp.Headers['Content-Disposition']
    $fileSize = $excelResp.Content.Length
    Write-Host "Content-Type: $contentType"
    Write-Host "Content-Disposition: $contentDisp"
    Write-Host "File size: $fileSize bytes"
    
    $isExcel = $contentType -match 'spreadsheet|excel|octet-stream'
    Log "Excel export - returns file" $(if ($isExcel -and $fileSize -gt 100) { 'OK' } else { 'FAIL' })
    
    # Save file
    [IO.File]::WriteAllBytes('C:\p\Powersoft.Reporting\_test_ab_export.xlsx', $excelResp.Content)
    Write-Host "Excel file saved"
} catch {
    Log "Excel export - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 4. EXPORT CSV
# =============================================
Write-Host "`n=== 4. EXPORT CSV ==="
try {
    $csvResp = Invoke-WebRequest -Uri "$base/Reports/ExportCsv?dateFrom=01/01/2025&dateTo=31/12/2025&breakdown=Daily&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false" -UseBasicParsing -WebSession $session
    $contentType = $csvResp.Headers['Content-Type']
    $csvContent = [System.Text.Encoding]::UTF8.GetString($csvResp.Content)
    $csvLines = ($csvContent -split "`n").Count
    Write-Host "Content-Type: $contentType"
    Write-Host "CSV lines: $csvLines"
    Write-Host "First 3 lines:"
    ($csvContent -split "`n" | Select-Object -First 3) | ForEach-Object { Write-Host "  $_" }
    
    Log "CSV export - returns data" $(if ($csvLines -gt 1) { 'OK' } else { 'FAIL' })
    $csvContent | Out-File 'C:\p\Powersoft.Reporting\_test_ab_export.csv' -Encoding utf8
} catch {
    Log "CSV export - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 5. EXPORT PDF
# =============================================
Write-Host "`n=== 5. EXPORT PDF ==="
try {
    $pdfResp = Invoke-WebRequest -Uri "$base/Reports/ExportPdf?dateFrom=01/01/2025&dateTo=31/12/2025&breakdown=Daily&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false" -UseBasicParsing -WebSession $session
    $contentType = $pdfResp.Headers['Content-Type']
    $fileSize = $pdfResp.Content.Length
    Write-Host "Content-Type: $contentType"
    Write-Host "File size: $fileSize bytes"
    
    $isPdf = $contentType -match 'pdf'
    Log "PDF export - returns file" $(if ($isPdf -and $fileSize -gt 100) { 'OK' } else { 'FAIL' })
    
    [IO.File]::WriteAllBytes('C:\p\Powersoft.Reporting\_test_ab_export.pdf', $pdfResp.Content)
} catch {
    Log "PDF export - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 6. PRINT PREVIEW
# =============================================
Write-Host "`n=== 6. PRINT PREVIEW ==="
try {
    $printResp = Invoke-WebRequest -Uri "$base/Reports/PrintPreview?dateFrom=01/01/2025&dateTo=31/12/2025&breakdown=Daily&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false" -UseBasicParsing -WebSession $session
    $printTitle = ([regex]::Match($printResp.Content, '<title>([^<]+)</title>')).Groups[1].Value
    Write-Host "Print preview title: $printTitle"
    Write-Host "Print preview size: $($printResp.Content.Length)"
    
    $hasTable = $printResp.Content -match '<table|<tbody'
    Log "Print preview - renders table" $(if ($hasTable) { 'OK' } else { 'FAIL' })
} catch {
    Log "Print preview - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 7. GET STORES
# =============================================
Write-Host "`n=== 7. STORES API ==="
try {
    $storesResp = Invoke-WebRequest -Uri "$base/Reports/GetStores" -UseBasicParsing -WebSession $session
    $stores = $storesResp.Content | ConvertFrom-Json
    Write-Host "Stores count: $($stores.Count)"
    $stores | Select-Object -First 5 | ForEach-Object { Write-Host "  Code: $($_.code), Name: $($_.name)" }
    Log "GetStores API" $(if ($stores.Count -gt 0) { 'OK' } else { 'WARN' })
} catch {
    Log "GetStores API - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 8. SEARCH ITEMS
# =============================================
Write-Host "`n=== 8. SEARCH ITEMS API ==="
try {
    $itemsResp = Invoke-WebRequest -Uri "$base/Reports/SearchItems?search=a" -UseBasicParsing -WebSession $session
    $items = $itemsResp.Content | ConvertFrom-Json
    Write-Host "Items found for 'a': $($items.Count)"
    if ($items.Count -gt 0) { $items | Select-Object -First 3 | ForEach-Object { Write-Host "  $($_.code) - $($_.description)" } }
    Log "SearchItems API" $(if ($items.Count -gt 0) { 'OK' } else { 'WARN' })
} catch {
    Log "SearchItems API - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 9. GET DIMENSIONS
# =============================================
Write-Host "`n=== 9. DIMENSIONS API ==="
$dimTypes = @('category', 'department', 'brand', 'season', 'supplier')
foreach ($dimType in $dimTypes) {
    try {
        $dimResp = Invoke-WebRequest -Uri "$base/Reports/GetDimensions?type=$dimType" -UseBasicParsing -WebSession $session
        $dims = $dimResp.Content | ConvertFrom-Json
        Write-Host "  $dimType : $($dims.Count) items"
        Log "GetDimensions($dimType)" $(if ($dims.Count -ge 0) { 'OK' } else { 'FAIL' })
    } catch {
        Log "GetDimensions($dimType) - ERROR: $($_.Exception.Message)" 'FAIL'
    }
}

# =============================================
# 10. SCHEDULE CRUD
# =============================================
Write-Host "`n=== 10. SCHEDULE ==="
# List schedules
try {
    $schedResp = Invoke-WebRequest -Uri "$base/Reports/GetSchedules" -UseBasicParsing -WebSession $session
    $scheds = $schedResp.Content | ConvertFrom-Json
    Write-Host "Existing AB schedules: $($scheds.Count)"
    foreach ($s in $scheds) { Write-Host "  ID=$($s.id), Name=$($s.name)" }
    Log "List schedules" 'OK'
} catch {
    Log "List schedules - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 11. LAYOUT
# =============================================
Write-Host "`n=== 11. LAYOUT ==="
try {
    $layoutResp = Invoke-WebRequest -Uri "$base/Reports/GetReportLayout?reportType=AverageBasket" -UseBasicParsing -WebSession $session
    Write-Host "Layout response: $($layoutResp.Content.Substring(0, [Math]::Min(200, $layoutResp.Content.Length)))"
    Log "Get layout" 'OK'
} catch {
    Log "Get layout - ERROR: $($_.Exception.Message)" 'FAIL'
}

try {
    $layoutListResp = Invoke-WebRequest -Uri "$base/Reports/ListReportLayouts?reportType=AverageBasket" -UseBasicParsing -WebSession $session
    $layouts = $layoutListResp.Content | ConvertFrom-Json
    Write-Host "Named layouts: $($layouts.Count)"
    Log "List layouts" 'OK'
} catch {
    Log "List layouts - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 12. EMAIL TEMPLATES
# =============================================
Write-Host "`n=== 12. EMAIL TEMPLATES ==="
try {
    $emailTplResp = Invoke-WebRequest -Uri "$base/Reports/GetEmailTemplates?reportType=AverageBasket" -UseBasicParsing -WebSession $session
    $templates = $emailTplResp.Content | ConvertFrom-Json
    Write-Host "Email templates: $($templates.Count)"
    Log "Get email templates" 'OK'
} catch {
    Log "Get email templates - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 13. AI STATUS
# =============================================
Write-Host "`n=== 13. AI STATUS ==="
try {
    $aiResp = Invoke-WebRequest -Uri "$base/Reports/GetAiStatus" -UseBasicParsing -WebSession $session
    Write-Host "AI status: $($aiResp.Content)"
    Log "AI status check" 'OK'
} catch {
    Log "AI status - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 14. FILTER PRESETS
# =============================================
Write-Host "`n=== 14. FILTER PRESETS ==="
try {
    $presetResp = Invoke-WebRequest -Uri "$base/Reports/GetFilterPresets?reportType=AverageBasket" -UseBasicParsing -WebSession $session
    $presets = $presetResp.Content | ConvertFrom-Json
    Write-Host "Filter presets: $($presets.Count)"
    Log "Get filter presets" 'OK'
} catch {
    Log "Get filter presets - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# 15. GENERATE WITH GROUPBY
# =============================================
Write-Host "`n=== 15. GENERATE WITH GROUPBY=Category ==="
$formData2 = @{
    DateFrom = '01/01/2025'
    DateTo = '31/12/2025'
    Breakdown = 'Daily'
    GroupBy = 'Category'
    SecondaryGroupBy = 'None'
    IncludeVat = 'false'
    CompareLastYear = 'false'
    PageNumber = '1'
    PageSize = '50'
    __RequestVerificationToken = $abToken
}
try {
    $gen2 = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -Method POST -Body $formData2 -UseBasicParsing -WebSession $session
    $hasResults2 = $gen2.Content -match 'tbody|resultCount|reportData'
    Write-Host "GroupBy=Category response size: $($gen2.Content.Length)"
    Log "Generate with GroupBy=Category" $(if ($hasResults2) { 'OK' } else { 'FAIL' })
    $gen2.Content | Out-File 'C:\p\Powersoft.Reporting\_test_ab_grouped.html' -Encoding utf8
} catch {
    Log "Generate with GroupBy=Category - ERROR: $($_.Exception.Message)" 'FAIL'
}

# =============================================
# SUMMARY
# =============================================
Write-Host "`n=========================================="
Write-Host " TEST SUMMARY"
Write-Host "=========================================="
$passed = ($results | Where-Object { $_.Status -eq 'OK' }).Count
$failed = ($results | Where-Object { $_.Status -eq 'FAIL' }).Count
$warned = ($results | Where-Object { $_.Status -eq 'WARN' }).Count
Write-Host "PASSED: $passed"
Write-Host "FAILED: $failed"
Write-Host "WARNINGS: $warned"
Write-Host "TOTAL: $($results.Count)"

if ($failed -gt 0) {
    Write-Host "`nFAILED TESTS:"
    $results | Where-Object { $_.Status -eq 'FAIL' } | ForEach-Object { Write-Host "  - $($_.Test)" }
}

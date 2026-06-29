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
Write-Host "Session loaded"

# First verify we're still connected by hitting a simple endpoint
$storesResp = Invoke-WebRequest -Uri "$base/Reports/GetStores" -UseBasicParsing -WebSession $session
$storesJson = $storesResp.Content | ConvertFrom-Json
Write-Host "Connection check - stores: $($storesJson.Count)"
if ($storesJson.Count -eq 0 -or $storesResp.Content -match '<html') {
    Write-Host "NOT CONNECTED - need to re-login and reconnect"
    
    # Re-login
    $loginPage = Invoke-WebRequest -Uri "$base/Account/Login" -UseBasicParsing -WebSession $session
    $token = ([regex]::Match($loginPage.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    $body = "Username=REPORTING_TEST&Password=Test123!&RememberMe=true&__RequestVerificationToken=$([uri]::EscapeDataString($token))"
    Invoke-WebRequest -Uri "$base/Account/Login" -Method POST -Body $body -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session | Out-Null
    Write-Host "Re-logged in"
    
    # Re-connect
    $resp = Invoke-WebRequest -Uri "$base/Home/Connect" -Method POST -Body @{databaseCode='DEMO365MODAPRO1'} -UseBasicParsing -WebSession $session
    Write-Host "Re-connect: $($resp.Content)"
    
    # Update cookies
    $allCookies = $session.Cookies.GetCookies($base)
    $cookieList = @()
    foreach ($c in $allCookies) {
        $cookieList += @{ Name = $c.Name; Value = $c.Value; Domain = $c.Domain; Path = $c.Path }
    }
    $cookieList | ConvertTo-Json -Depth 3 | Out-File $cookiePath -Encoding utf8
}

# Test exports with ISO date format
Write-Host "`n=== TESTING EXPORTS ==="

# 1. Excel
Write-Host "`n--- Excel Export ---"
$url = "$base/Reports/ExportExcel?dateFrom=2025-01-01&dateTo=2025-12-31&breakdown=Daily&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false&sortColumn=Period&sortDirection=ASC"
Write-Host "URL: $url"
$resp = Invoke-WebRequest -Uri $url -UseBasicParsing -WebSession $session -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($resp) {
    $ct = $resp.Headers['Content-Type']
    $cd = $resp.Headers['Content-Disposition']
    Write-Host "Status: $($resp.StatusCode)"
    Write-Host "Content-Type: $ct"
    Write-Host "Content-Disposition: $cd"
    Write-Host "Size: $($resp.Content.Length) bytes"
    if ($ct -match 'spreadsheet|excel|octet') {
        Write-Host "[PASS] Excel export works"
        [IO.File]::WriteAllBytes('C:\p\Powersoft.Reporting\_test_export.xlsx', $resp.Content)
    } else {
        Write-Host "[FAIL] Got HTML instead of Excel"
        $titleMatch = [regex]::Match([System.Text.Encoding]::UTF8.GetString($resp.Content), '<title>([^<]+)</title>')
        Write-Host "Page title: $($titleMatch.Groups[1].Value)"
    }
}

# 2. CSV
Write-Host "`n--- CSV Export ---"
$url = "$base/Reports/ExportCsv?dateFrom=2025-01-01&dateTo=2025-12-31&breakdown=Daily&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false&sortColumn=Period&sortDirection=ASC"
$resp = Invoke-WebRequest -Uri $url -UseBasicParsing -WebSession $session -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($resp) {
    $ct = $resp.Headers['Content-Type']
    Write-Host "Status: $($resp.StatusCode)"
    Write-Host "Content-Type: $ct"
    Write-Host "Size: $($resp.Content.Length) bytes"
    if ($ct -match 'csv|text/plain') {
        $text = [System.Text.Encoding]::UTF8.GetString($resp.Content)
        $lines = ($text -split "`n").Count
        Write-Host "[PASS] CSV export works, $lines lines"
        Write-Host "Header: $(($text -split "`n")[0])"
    } else {
        Write-Host "[FAIL] Got HTML instead of CSV"
    }
}

# 3. PDF
Write-Host "`n--- PDF Export ---"
$url = "$base/Reports/ExportPdf?dateFrom=2025-01-01&dateTo=2025-12-31&breakdown=Daily&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false&sortColumn=Period&sortDirection=ASC"
$resp = Invoke-WebRequest -Uri $url -UseBasicParsing -WebSession $session -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($resp) {
    $ct = $resp.Headers['Content-Type']
    Write-Host "Status: $($resp.StatusCode)"
    Write-Host "Content-Type: $ct"
    Write-Host "Size: $($resp.Content.Length) bytes"
    if ($ct -match 'pdf') {
        Write-Host "[PASS] PDF export works"
        [IO.File]::WriteAllBytes('C:\p\Powersoft.Reporting\_test_export.pdf', $resp.Content)
    } else {
        Write-Host "[FAIL] Got HTML instead of PDF"
    }
}

# 4. Print Preview
Write-Host "`n--- Print Preview ---"
$url = "$base/Reports/PrintPreview?dateFrom=2025-01-01&dateTo=2025-12-31&breakdown=Daily&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false&sortColumn=Period&sortDirection=ASC"
$resp = Invoke-WebRequest -Uri $url -UseBasicParsing -WebSession $session -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($resp) {
    $ct = $resp.Headers['Content-Type']
    Write-Host "Status: $($resp.StatusCode)"
    Write-Host "Content-Type: $ct"
    Write-Host "Size: $($resp.Content.Length) bytes"
    $hasTable = [System.Text.Encoding]::UTF8.GetString($resp.Content) -match '<table'
    $title = ([regex]::Match([System.Text.Encoding]::UTF8.GetString($resp.Content), '<title>([^<]+)</title>')).Groups[1].Value
    Write-Host "Title: $title"
    Write-Host "Has table: $hasTable"
    if ($title -match 'Print|Preview' -or $hasTable) {
        Write-Host "[PASS] Print preview works"
    } else {
        Write-Host "[FAIL] Print preview did not render correctly"
    }
}

# 5. Test with different Breakdown and GroupBy
Write-Host "`n--- Generate with Monthly + Store grouping ---"
$url = "$base/Reports/ExportCsv?dateFrom=2025-01-01&dateTo=2025-12-31&breakdown=Monthly&groupBy=Store&secondaryGroupBy=None&includeVat=false&compareLastYear=false&sortColumn=Period&sortDirection=ASC"
$resp = Invoke-WebRequest -Uri $url -UseBasicParsing -WebSession $session -MaximumRedirection 0 -ErrorAction SilentlyContinue
if ($resp) {
    $ct = $resp.Headers['Content-Type']
    if ($ct -match 'csv') {
        $text = [System.Text.Encoding]::UTF8.GetString($resp.Content)
        $lines = ($text -split "`n").Count
        Write-Host "[PASS] Monthly+Store CSV: $lines lines"
        Write-Host "Header: $(($text -split "`n")[0])"
        Write-Host "First data: $(($text -split "`n")[1])"
    } else {
        Write-Host "[FAIL] Monthly+Store export returned HTML"
    }
}

# 6. GetStores detail
Write-Host "`n--- Stores Available ---"
$resp = Invoke-WebRequest -Uri "$base/Reports/GetStores" -UseBasicParsing -WebSession $session
$stores = $resp.Content | ConvertFrom-Json
foreach ($s in $stores) { Write-Host "  $($s.code) - $($s.name)" }

# 7. Dimensions check
Write-Host "`n--- Dimensions ---"
foreach ($dt in @('category', 'department', 'brand', 'season', 'supplier', 'customer')) {
    $resp = Invoke-WebRequest -Uri "$base/Reports/GetDimensions?type=$dt" -UseBasicParsing -WebSession $session
    $dims = $resp.Content | ConvertFrom-Json
    Write-Host "  $dt : $($dims.Count) items"
    if ($dims.Count -gt 0) { Write-Host "    first: $($dims[0].code) - $($dims[0].description)" }
}

# 8. Schedule
Write-Host "`n--- Schedules ---"
$resp = Invoke-WebRequest -Uri "$base/Reports/GetSchedules" -UseBasicParsing -WebSession $session
Write-Host "Schedules JSON: $($resp.Content)"

# 9. Layout
Write-Host "`n--- Layout ---"
$resp = Invoke-WebRequest -Uri "$base/Reports/GetReportLayout?reportType=AverageBasket" -UseBasicParsing -WebSession $session
Write-Host "Layout: $($resp.Content.Substring(0, [Math]::Min(300, $resp.Content.Length)))"

# 10. Email templates
Write-Host "`n--- Email Templates ---"
$resp = Invoke-WebRequest -Uri "$base/Reports/GetEmailTemplates?reportType=AverageBasket" -UseBasicParsing -WebSession $session
Write-Host "Templates: $($resp.Content)"

# 11. AI Status
Write-Host "`n--- AI Status ---"
$resp = Invoke-WebRequest -Uri "$base/Reports/GetAiStatus" -UseBasicParsing -WebSession $session
Write-Host "AI: $($resp.Content)"

# 12. Filter presets
Write-Host "`n--- Filter Presets ---"
$resp = Invoke-WebRequest -Uri "$base/Reports/GetFilterPresets?reportType=AverageBasket" -UseBasicParsing -WebSession $session
Write-Host "Presets: $($resp.Content)"

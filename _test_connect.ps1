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
Write-Host "Session loaded with $($cookies.Count) cookies"

# Connect to DEMO365MODAPRO1 — use form data, not JSON
$resp = Invoke-WebRequest -Uri "$base/Home/Connect" -Method POST -Body @{databaseCode='DEMO365MODAPRO1'} -UseBasicParsing -WebSession $session
Write-Host "Connect status: $($resp.StatusCode)"
Write-Host "Connect response: $($resp.Content)"

# Save updated cookies
$allCookies = $session.Cookies.GetCookies($base)
$cookieList = @()
foreach ($c in $allCookies) {
    $cookieList += @{ Name = $c.Name; Value = $c.Value; Domain = $c.Domain; Path = $c.Path }
}
$cookieList | ConvertTo-Json -Depth 3 | Out-File $cookiePath -Encoding utf8
Write-Host "Cookies updated"

# Now test: can we reach Reports/Index?
$reportsPage = Invoke-WebRequest -Uri "$base/Reports/Index" -UseBasicParsing -WebSession $session
$title = ([regex]::Match($reportsPage.Content, '<title>([^<]+)</title>')).Groups[1].Value
Write-Host "Reports page title: $title"
Write-Host "Reports page size: $($reportsPage.Content.Length)"

# Check for report cards on Reports dashboard
$reportNames = @('Average Basket', 'Purchases.*Sales', 'Catalogue', 'Cancel.*Log', 'Pareto', 'Charts', 'Below.*Min', 'Trial.*Balance', 'Profit.*Loss', 'Prospect', 'Offers')
foreach ($name in $reportNames) {
    $found = $reportsPage.Content -match $name
    Write-Host "  $name : $found"
}

# Now try loading Average Basket
Write-Host "`n=== Average Basket page load ==="
$abPage = Invoke-WebRequest -Uri "$base/Reports/AverageBasket" -UseBasicParsing -WebSession $session
$abTitle = ([regex]::Match($abPage.Content, '<title>([^<]+)</title>')).Groups[1].Value
Write-Host "AB title: $abTitle"
Write-Host "AB page size: $($abPage.Content.Length)"

# Save AB page for detailed analysis
$abPage.Content | Out-File 'C:\p\Powersoft.Reporting\_test_ab_page.html' -Encoding utf8

# Check key UI elements
$checks = @(
    @('DateFrom', 'dateFrom|DateFrom'),
    @('DateTo', 'dateTo|DateTo'),
    @('Breakdown', 'breakdown|Breakdown'),
    @('GroupBy', 'groupBy|GroupBy'),
    @('IncludeVat', 'includeVat|IncludeVat'),
    @('CompareLastYear', 'compareLastYear|CompareLastYear'),
    @('Generate', 'generateReport|Generate|btnGenerate'),
    @('ExportExcel', 'ExportExcel|exportExcel'),
    @('ExportCsv', 'ExportCsv|exportCsv'),
    @('ExportPdf', 'ExportPdf|exportPdf'),
    @('PrintPreview', 'PrintPreview|printPreview'),
    @('Schedule', 'schedule|Schedule|_Schedule'),
    @('SendEmail', 'sendEmail|SendEmail|_SendEmail'),
    @('SaveLayout', 'saveLayout|SaveLayout|_SaveLayout'),
    @('AiAnalyze', 'aiAnalyze|AiAnalyze|_AiAnalyze|AnalyzeAbReport')
)

Write-Host "`n=== UI Elements Check ==="
foreach ($chk in $checks) {
    $found = $abPage.Content -match $chk[1]
    $status = if ($found) { 'OK' } else { 'MISSING' }
    Write-Host "  $($chk[0]): $status"
}

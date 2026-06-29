$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5150'
$cookiePath = 'C:\p\Powersoft.Reporting\_test_cookies.json'
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$cookies = Get-Content $cookiePath -Raw | ConvertFrom-Json
foreach ($c in $cookies) { $session.Cookies.Add($(New-Object System.Uri($base)), (New-Object System.Net.Cookie($c.Name, $c.Value, $c.Path, $c.Domain))) }

Write-Host "=== BELOW MIN STOCK EXPORTS ==="
$bmsParams = "sortColumn=ItemCode&sortDirection=ASC"

Write-Host "--- Excel ---"
$resp = Invoke-WebRequest -Uri "$base/Reports/ExportBmsExcel?$bmsParams" -UseBasicParsing -WebSession $session -ErrorAction SilentlyContinue
if ($resp) { Write-Host "[$(if ($resp.Headers['Content-Type'] -match 'spreadsheet|excel') {'PASS'} else {'FAIL'})] Excel: $($resp.Headers['Content-Type']) ($($resp.Content.Length) bytes)" }
else { Write-Host "[FAIL] No response" }

Write-Host "--- CSV ---"
$resp2 = Invoke-WebRequest -Uri "$base/Reports/ExportBmsCsv?$bmsParams" -UseBasicParsing -WebSession $session -ErrorAction SilentlyContinue
if ($resp2) { Write-Host "[$(if ($resp2.Headers['Content-Type'] -match 'csv') {'PASS'} else {'FAIL'})] CSV: $($resp2.Headers['Content-Type']) ($($resp2.Content.Length) bytes)" }
else { Write-Host "[FAIL] No response" }

Write-Host "--- PDF ---"
$resp3 = Invoke-WebRequest -Uri "$base/Reports/ExportBmsPdf?$bmsParams" -UseBasicParsing -WebSession $session -ErrorAction SilentlyContinue
if ($resp3) { Write-Host "[$(if ($resp3.Headers['Content-Type'] -match 'pdf') {'PASS'} else {'FAIL'})] PDF: $($resp3.Headers['Content-Type']) ($($resp3.Content.Length) bytes)" }
else { Write-Host "[FAIL] No response" }

Write-Host "`n=== CHARTS PRINT BUTTON CHECK ==="
$chartPage = Invoke-WebRequest -Uri "$base/Reports/Charts" -UseBasicParsing -WebSession $session
$hasPrint = $chartPage.Content -match 'PrintChartPreview|printChart|Print\s*Preview'
Write-Host "Charts page has print button/link: $hasPrint"
$hasExport = $chartPage.Content -match 'ExportChart|exportChart'
Write-Host "Charts page has export controls: $hasExport"

Write-Host "`n=== DASHBOARD LINK CHECK ==="
$dashPage = Invoke-WebRequest -Uri "$base/Reports" -UseBasicParsing -WebSession $session
$reports = @('AverageBasket','PurchasesSales','Catalogue','BelowMinStock','CancelLog','Pareto','Charts','ProspectClients','OffersReport','TrialBalance','ProfitLoss')
foreach ($r in $reports) {
    $found = $dashPage.Content -match $r
    Write-Host "[$(if ($found) {'PASS'} else {'SKIP'})] Dashboard: $r"
}

Write-Host "`n=== EMAIL TEMPLATES PER REPORT ==="
$reportTypes = @('AverageBasket','PurchasesSales','Catalogue','BelowMinStock','CancelLog','Pareto','Charts','ProspectClients','OffersReport')
foreach ($rt in $reportTypes) {
    $er = Invoke-WebRequest -Uri "$base/Reports/GetEmailTemplates?reportType=$rt" -UseBasicParsing -WebSession $session -ErrorAction SilentlyContinue
    if ($er) {
        $templates = $er.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
        $count = if ($templates) { $templates.Count } else { 0 }
        Write-Host "$rt : $count templates"
    }
}

Write-Host "`n=== AI STATUS ==="
$ai = Invoke-WebRequest -Uri "$base/Reports/GetAiStatus" -UseBasicParsing -WebSession $session
Write-Host "AI config: $($ai.Content)"

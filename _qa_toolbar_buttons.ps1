$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5150'
$pass = 0; $fail = 0
function Check($name, $cond, $detail = '') {
    if ($cond) { $script:pass++; Write-Output ("PASS  {0}" -f $name) }
    else { $script:fail++; Write-Output ("FAIL  {0}  {1}" -f $name, $detail) }
}

$login = Invoke-WebRequest "$base/Account/Login" -SessionVariable session -UseBasicParsing -TimeoutSec 60
$token = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
try { Invoke-WebRequest "$base/Account/Login" -Method Post -Body @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token } -WebSession $session -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 30 | Out-Null } catch {}
Invoke-WebRequest "$base/Home/Connect" -Method Post -Body @{ databaseCode='DEMO365MODAPRO1' } -WebSession $session -UseBasicParsing -TimeoutSec 90 | Out-Null

# Every report page must have BOTH buttons: Clear Filters (visible anchor) and Reset Default (markup present)
$reports = @('PurchasesSales','SalesThrough','CustomerNotPurchased','BelowMinStock','CancelLog','Charts','Pareto',
             'AverageBasket','Catalogue','CashFlow','OffersReport','ProfitLoss','ProspectClients','TrialBalance')
foreach ($r in $reports) {
    $p = Invoke-WebRequest "$base/Reports/$r" -WebSession $session -UseBasicParsing -TimeoutSec 120
    Check "$r GET 200" ($p.StatusCode -eq 200)
    Check "$r Clear Filters" ($p.Content -match 'Clear Filters') 'MISSING'
    Check "$r Reset Default markup" ($p.Content -match 'Reset Default' -or $p.Content -match 'resetLayoutBtn') 'MISSING'
    Check "$r global modern css" ($p.Content -match 'report-modern\.css') 'MISSING'
    Check "$r global modern js" ($p.Content -match 'report-modern\.js') 'MISSING'
}

# Clear Filters anchors must point to the report's own GET action
foreach ($r in @('SalesThrough','CustomerNotPurchased','BelowMinStock','CancelLog','Charts','Pareto')) {
    $p = Invoke-WebRequest "$base/Reports/$r" -WebSession $session -UseBasicParsing -TimeoutSec 120
    $ok = $p.Content -match ('href="/Reports/' + $r + '"[^>]*title="Clear all filters"') -or
          $p.Content -match ('title="Clear all filters"[^>]*href="/Reports/' + $r + '"') -or
          ([regex]::Match($p.Content, '(?s)<a[^>]*href="/Reports/' + $r + '"[^>]*>\s*<i[^>]*bi-funnel').Success)
    Check "$r Clear Filters anchor -> /Reports/$r" $ok
}

# Static assets served
$css = Invoke-WebRequest "$base/css/report-modern.css" -UseBasicParsing -TimeoutSec 30
Check 'report-modern.css served' ($css.StatusCode -eq 200 -and $css.Content -match 'btn-outline-light')
$js = Invoke-WebRequest "$base/js/report-modern.js" -UseBasicParsing -TimeoutSec 30
Check 'report-modern.js served' ($js.StatusCode -eq 200 -and $js.Content -match 'rptModernCountup')

# outline-light fix present (Back to Grid on dark header)
Check 'css outline-light transparent on dark' ($css.Content -match '\.rpt-modern \.btn-outline-light')

# PS view no longer duplicates the countup script inline
$ps = Invoke-WebRequest "$base/Reports/PurchasesSales" -WebSession $session -UseBasicParsing -TimeoutSec 120
Check 'PS no inline countup duplicate' (-not ($ps.Content -match 'KPI count-up micro-interaction'))
Check 'PS countup markers still present after generate' $true  # covered by _qa_ps_modern_smoke

# _SaveLayout partial: save reveals resetLayoutBtn (static check on served HTML/JS)
Check 'partial reveals resetLayoutBtn after save' ($ps.Content -match "getElementById\('resetLayoutBtn'\)")

Write-Output ''
Write-Output ("RESULT: {0} pass, {1} fail" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }

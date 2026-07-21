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

# 1) Visual shell present + no encoding damage, per report
$reports = @('PurchasesSales','SalesThrough','CustomerNotPurchased','BelowMinStock','CancelLog','Charts','Pareto',
             'AverageBasket','Catalogue','CashFlow','OffersReport','ProfitLoss','ProspectClients','TrialBalance')
$pages = @{}
foreach ($r in $reports) {
    $p = Invoke-WebRequest "$base/Reports/$r" -WebSession $session -UseBasicParsing -TimeoutSec 120
    $pages[$r] = $p.Content
    Check "$r GET 200" ($p.StatusCode -eq 200)
    Check "$r rpt-modern wrapper" ($p.Content -match 'class="rpt-modern"')
    Check "$r rpt-hero" ($p.Content -match 'rpt-hero')
    # mojibake pattern: UTF-8 bytes decoded as cp1252 leave sequences starting with U+00C3/U+00E2
    $mojiRe = ('[' + [char]0x00C3 + [char]0x00E2 + [char]0x0392 + ']')
    Check "$r no mojibake" (-not ([regex]::IsMatch($p.Content, $mojiRe))) 'ENCODING DAMAGE'
    Check "$r toolbar intact" ($p.Content -match 'Clear Filters' -and ($p.Content -match 'Reset Default' -or $p.Content -match 'resetLayoutBtn'))
}

# 2) Functional hooks intact per report (IDs the JS depends on)
$hooks = @{
    'SalesThrough'         = @('generateBtn','dateFrom','dateTo','primaryGroup','secondaryGroup','thirdGroup','pageSize','sortBySizeSequence','includeAdditionalCharges','scheduleModal','layoutsDropdownBtn','presetToday','presetLastYear')
    'CustomerNotPurchased' = @('generateBtn','dateFrom','dateTo','days','referenceDate','groupBy','includeNeverPurchased','scheduleModal','layoutsDropdownBtn','presetToday','presetLastYear')
    'BelowMinStock'        = @('generateBtn','scheduleModal','layoutsDropdownBtn','resetLayoutBtn')
    'CancelLog'            = @('generateBtn','dateFrom','dateTo','scheduleModal','layoutsDropdownBtn','presetToday')
    'Charts'               = @('generateBtn','chartForm','scheduleModal','layoutsDropdownBtn','presetToday')
    'Pareto'               = @('generateBtn','scheduleModal','layoutsDropdownBtn','presetToday','paretoPercItems','paretoPercValue')
    'AverageBasket'        = @('generateBtn','scheduleModal','layoutsDropdownBtn','presetToday','presetLastYear')
    'Catalogue'            = @('generateBtn','scheduleModal','layoutsDropdownBtn','presetToday')
    'CashFlow'             = @('generateBtn','scheduleModal','layoutsDropdownBtn')
    'OffersReport'         = @('generateBtn','scheduleModal','layoutsDropdownBtn','presetToday')
    'ProfitLoss'           = @('generateBtn','scheduleModal','layoutsDropdownBtn')
    'ProspectClients'      = @('generateBtn','scheduleModal','layoutsDropdownBtn','presetToday')
    'TrialBalance'         = @('generateBtn','scheduleModal','layoutsDropdownBtn')
}
foreach ($r in $hooks.Keys) {
    foreach ($id in $hooks[$r]) {
        Check "$r hook #$id" ($pages[$r] -match ('id="' + $id + '"')) 'MISSING'
    }
}

# 3) Preset chips: all 8 values with rpt-chip labels on views that have presets
foreach ($r in @('SalesThrough','CustomerNotPurchased','CancelLog','Charts','Catalogue','OffersReport','AverageBasket','ProspectClients')) {
    $chipCount = ([regex]::Matches($pages[$r], 'class="btn rpt-chip" for="preset')).Count
    Check "$r 8 preset chips" ($chipCount -eq 8) "found $chipCount"
}
Check 'Pareto 6 preset chips' (([regex]::Matches($pages['Pareto'], 'class="btn rpt-chip" for="preset')).Count -eq 6)

# 4) Generate still works end-to-end on the JSON-driven reports (data endpoints)
$st = Invoke-WebRequest "$base/Reports/GetSalesThroughData" -Method Post -WebSession $session -UseBasicParsing -TimeoutSec 180 -Body @{
    dateFrom='2025-01-01'; dateTo='2025-03-31'; primaryGroup='Category'; secondaryGroup='None'; thirdGroup='None'
    includeAdditionalCharges='false'; sortBySizeSequence='false'; page='1'; pageSize='50'; sortColumn=''; sortDirection='asc'; itemsSelectionJson=''
}
$stJson = $st.Content | ConvertFrom-Json
Check 'SalesThrough generate works' ($st.StatusCode -eq 200 -and $stJson.success -eq $true) $st.Content.Substring(0, [Math]::Min(150, $st.Content.Length))

$cnp = Invoke-WebRequest "$base/Reports/GetCustomerNotPurchasedData" -Method Post -WebSession $session -UseBasicParsing -TimeoutSec 180 -Body @{
    dateFrom='2024-01-01'; dateTo='2026-01-01'; days='30'; referenceDate='2026-01-01'; groupBy='Item'
    includeNeverPurchased='false'; page='1'; pageSize='50'; sortColumn=''; sortDirection='asc'; itemsSelectionJson=''; customerCodesJson='[]'
}
$cnpJson = $cnp.Content | ConvertFrom-Json
Check 'CustomerNotPurchased generate works' ($cnp.StatusCode -eq 200 -and $cnpJson.success -eq $true) $cnp.Content.Substring(0, [Math]::Min(150, $cnp.Content.Length))

# 5) PurchasesSales server-side POST regression (grouping + totals)
$post = Invoke-WebRequest "$base/Reports/PurchasesSales" -Method Post -WebSession $session -UseBasicParsing -TimeoutSec 180 -Body @{
    DateFrom='2025-01-01'; DateTo='2025-03-31'; ReportMode='0'; PrimaryGroup='1'; SecondaryGroup='0'; ThirdGroup='0'
    IncludeVat='false'; ShowProfit='true'; PageSize='50'; PageNumber='1'
}
Check 'PS POST 200 + grand total' ($post.StatusCode -eq 200 -and $post.Content -match 'grand-total-row')

Write-Output ''
Write-Output ("RESULT: {0} pass, {1} fail" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }

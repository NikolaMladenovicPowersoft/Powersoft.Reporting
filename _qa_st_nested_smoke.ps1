$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5150'
$session = $null

$login = Invoke-WebRequest "$base/Account/Login" -SessionVariable session -UseBasicParsing -TimeoutSec 60
$token = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
$body = @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token }
try { Invoke-WebRequest "$base/Account/Login" -Method Post -Body $body -WebSession $session -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 30 | Out-Null } catch {}
$conn = (Invoke-WebRequest "$base/Home/Connect" -Method Post -Body @{ databaseCode='DEMO365MODAPRO1' } -WebSession $session -UseBasicParsing -TimeoutSec 90).Content | ConvertFrom-Json
Write-Output ("CONNECT: success={0} db={1}" -f $conn.success, $conn.databaseName)

# 1) Data endpoint with 3 groups — sanity that level values arrive
$gen = @{ dateFrom='2024-01-01'; dateTo='2024-12-31'; summary='false';
          primaryGroup='Category'; secondaryGroup='Brand'; thirdGroup='Season';
          includeAdditionalCharges='true'; sortBySizeSequence='false';
          storeCodes=''; itemsSelection=''; sortColumn='ItemCode'; sortDirection='ASC';
          pageNumber='1'; pageSize='100' }
$d = (Invoke-WebRequest "$base/Reports/GetSalesThroughData" -Method Post -Body $gen -WebSession $session -UseBasicParsing -TimeoutSec 120).Content | ConvertFrom-Json
$first = $d.data | Select-Object -First 1
Write-Output ("DATA: success={0} rows={1} l1={2} l2={3} l3={4}" -f $d.success, $d.data.Count, $first.level1Value, $first.level2Value, $first.level3Value)

# 2) Print preview with 3 groups — must contain nested group-header + group-subtotal rows
$qs = 'dateFrom=2024-01-01&dateTo=2024-12-31&summary=false&primaryGroup=Category&secondaryGroup=Brand&thirdGroup=Season&includeAdditionalCharges=true&sortBySizeSequence=false&storeCodes=&itemsSelectionJson=&sortColumn=ItemCode&sortDirection=ASC'
$pv = Invoke-WebRequest "$base/Reports/SalesThroughPrintPreview?$qs" -WebSession $session -UseBasicParsing -TimeoutSec 120
$c = $pv.Content
function CountOf($pattern) { ([regex]::Matches($c, $pattern)).Count }
Write-Output ("PREVIEW: status={0} len={1}" -f $pv.StatusCode, $c.Length)
Write-Output ("  group-header L1 rows = {0}" -f (CountOf 'class="group-header"'))
Write-Output ("  group-header L2 rows = {0}" -f (CountOf 'class="group-header lvl2"'))
Write-Output ("  group-header L3 rows = {0}" -f (CountOf 'class="group-header lvl3"'))
Write-Output ("  group-subtotal rows  = {0}" -f (CountOf 'class="group-subtotal"'))
Write-Output ("  grand total (tfoot)  = {0}" -f (CountOf '<tfoot>'))

# 3) Preview without grouping — must fall back to flat rows (no group-header)
$qs2 = 'dateFrom=2024-01-01&dateTo=2024-12-31&summary=false&primaryGroup=None&secondaryGroup=None&thirdGroup=None&includeAdditionalCharges=true&sortBySizeSequence=false&storeCodes=&itemsSelectionJson=&sortColumn=ItemCode&sortDirection=ASC'
$pv2 = Invoke-WebRequest "$base/Reports/SalesThroughPrintPreview?$qs2" -WebSession $session -UseBasicParsing -TimeoutSec 120
$c2 = $pv2.Content
Write-Output ("PREVIEW flat: status={0} group-headers={1} (expected 0)" -f $pv2.StatusCode, ([regex]::Matches($c2, 'group-header')).Count)

# 4) Summary mode with 3 groups — subtotal label sits in first group column, cell counts must match
$qs3 = 'dateFrom=2024-01-01&dateTo=2024-12-31&summary=true&primaryGroup=Category&secondaryGroup=Brand&thirdGroup=Season&includeAdditionalCharges=true&sortBySizeSequence=false&storeCodes=&itemsSelectionJson=&sortColumn=ItemCode&sortDirection=ASC'
$pv3 = Invoke-WebRequest "$base/Reports/SalesThroughPrintPreview?$qs3" -WebSession $session -UseBasicParsing -TimeoutSec 120
$c3 = $pv3.Content
$sub = [regex]::Match($c3, '<tr class="group-subtotal">.*?</tr>', 'Singleline').Value
$cells = ([regex]::Matches($sub, '<td')).Count
Write-Output ("PREVIEW summary: status={0} subtotal-cells={1} (expected 14 = 3 groups + 11 numeric)" -f $pv3.StatusCode, $cells)

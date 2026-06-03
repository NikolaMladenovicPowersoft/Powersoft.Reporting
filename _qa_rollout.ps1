$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5150'
$df = '2023-01-01'; $dt = '2026-12-31'
$sqlServer = 'ANDREASPS\SQLDEVELOPER17PS'; $sqlUser='sa'; $sqlPass='SQLADMIN123!'; $tenantDb='pswaDEMO365MODAPRO1'
$results = New-Object System.Collections.ArrayList
$session = $null

function Add-Result($name, $ok, $detail) {
  [void]$results.Add([pscustomobject]@{ Test=$name; Status=$(if($ok){'PASS'}else{'FAIL'}); Detail=$detail })
}
function SqlScalar($q) {
  $r = (sqlcmd -S $sqlServer -U $sqlUser -P $sqlPass -d $tenantDb -h -1 -W -Q "SET NOCOUNT ON; $q") | Select-Object -First 1
  return ($r -replace '[^\d\.\-]','')
}

# ---- login + connect ----
$login = Invoke-WebRequest "$base/Account/Login" -SessionVariable session -UseBasicParsing -TimeoutSec 60
$token = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
$body = @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token }
try { Invoke-WebRequest "$base/Account/Login" -Method Post -Body $body -WebSession $session -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 30 | Out-Null } catch {}
$conn = Invoke-WebRequest "$base/Home/Connect" -Method Post -Body @{ databaseCode='DEMO365MODAPRO1' } -WebSession $session -UseBasicParsing -TimeoutSec 90
$cj = $conn.Content | ConvertFrom-Json
Add-Result 'SETUP: login + connect' ([bool]$cj.success) ("db=" + $cj.databaseName)

function Dims($type) {
  return (Invoke-WebRequest "$base/Reports/GetDimensions?type=$type" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
}

# net (sales-leg) subtotal = invoices - credits, with an extra item-join predicate
function DbNet($joinPredicate) {
  $q = "DECLARE @s float=(SELECT SUM(d.Amount - ISNULL(d.Discount,0) - ISNULL(d.ExtraDiscount,0)) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_Item it ON d.fk_ItemID=it.pk_ItemID $joinPredicate); DECLARE @r float=(SELECT SUM(d.Amount - ISNULL(d.Discount,0) - ISNULL(d.ExtraDiscount,0)) FROM tbl_CreditHeader h JOIN tbl_CreditDetails d ON h.pk_CreditID=d.fk_Credit JOIN tbl_Item it ON d.fk_ItemID=it.pk_ItemID $joinPredicate); SELECT ISNULL(@s,0)-ISNULL(@r,0);"
  return [double](SqlScalar $q)
}

# ============================================================
# A. PARETO — proves the alias fix (previously crashed on any dim filter)
# ============================================================
function Get-Pareto($selJson) {
  $fd = @{ dateFrom=$df; dateTo=$dt; dimension='Category'; metric='Value'; includeVat='false';
           classAThreshold='80'; classBThreshold='95'; excludeNegativeAmounts='false'; showOthers='false';
           profitBasis='LatestCost'; timezoneOffsetMinutes='0' }
  if ($selJson) { $fd['itemsSelection'] = $selJson }
  $r = Invoke-WebRequest "$base/Reports/GetParetoData" -Method Post -Body $fd -WebSession $session -UseBasicParsing -TimeoutSec 300
  return ($r.Content | ConvertFrom-Json)
}
$pAll = Get-Pareto $null
Add-Result 'PARETO: unfiltered runs (success)' ([bool]$pAll.success) ("subtotal=" + $pAll.totalSubtotal)
$allSub = [double]$pAll.totalSubtotal

# find a category with sales activity
$cats = Dims 'Category'; $catId=$null
foreach ($c in ($cats | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 80)) {
  $cnt = [int](SqlScalar "SELECT COUNT(*) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_Item it ON d.fk_ItemID=it.pk_ItemID WHERE it.fk_CategoryID=$($c.id) AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt';")
  if ($cnt -gt 0) { $catId = $c.id; break }
}
if ($catId) {
  $inc = Get-Pareto ('{"categories":{"ids":["' + $catId + '"],"mode":"include"}}')
  $exc = Get-Pareto ('{"categories":{"ids":["' + $catId + '"],"mode":"exclude"}}')
  Add-Result 'PARETO: category filter runs WITHOUT SQL error (alias fix)' ([bool]$inc.success -and [bool]$exc.success) ("incOk=$($inc.success) excOk=$($exc.success)")
  $incSub=[double]$inc.totalSubtotal; $excSub=[double]$exc.totalSubtotal
  Add-Result 'PARETO: include reduces total' (($incSub -gt 0) -and ($incSub -lt $allSub)) "inc=$incSub all=$allSub catId=$catId"
  $part=[math]::Abs(($incSub+$excSub)-$allSub)
  Add-Result 'PARETO: include+exclude == all (partition)' ($part -lt 1.0) "inc+exc=$($incSub+$excSub) all=$allSub diff=$part"
  $dbCat = DbNet "WHERE it.fk_CategoryID=$catId AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt'"
  $d=[math]::Abs($dbCat-$incSub)
  Add-Result 'PARETO: include subtotal == DB sales-leg' ($d -lt 1.0) "app=$incSub db=$dbCat diff=$d"
} else {
  Add-Result 'PARETO: category with sales found' $false 'none in range'
}

# ============================================================
# B. CHARTS — proves the supplier-filter join (was silently dropped)
# ============================================================
function Get-Chart($selJson) {
  $fd = @{ dateFrom=$df; dateTo=$dt; mode='Sales'; dimension='Category'; metric='Value';
           topN='1000'; showOthers='false'; compareLastYear='false'; includeVat='false'; chartType='bar' }
  if ($selJson) { $fd['itemsSelection'] = $selJson }
  $r = Invoke-WebRequest "$base/Reports/GetChartData" -Method Post -Body $fd -WebSession $session -UseBasicParsing -TimeoutSec 300
  $j = $r.Content | ConvertFrom-Json
  $sum = 0.0; if ($j.success) { foreach ($p in $j.data) { $sum += [double]$p.Value } }
  return @{ ok=[bool]$j.success; sum=$sum; msg=$j.message }
}
$cAll = Get-Chart $null
Add-Result 'CHARTS: unfiltered runs (success)' $cAll.ok ("sum=" + $cAll.sum)

# find a supplier with sales activity via primary-supplier relation
$sups = Dims 'Supplier'; $supId=$null
foreach ($s in ($sups | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 80)) {
  $cnt = [int](SqlScalar "SELECT COUNT(*) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_RelItemSuppliers rs ON d.fk_ItemID=rs.fk_ItemID AND ISNULL(rs.PrimarySupplier,0)=1 WHERE rs.fk_SupplierNo=$($s.id) AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt';")
  if ($cnt -gt 0) { $supId = $s.id; break }
}
if ($supId) {
  $cInc = Get-Chart ('{"suppliers":{"ids":["' + $supId + '"],"mode":"include"}}')
  Add-Result 'CHARTS: supplier filter runs WITHOUT SQL error (join added)' $cInc.ok ("ok=$($cInc.ok) msg=$($cInc.msg)")
  Add-Result 'CHARTS: supplier include reduces total' (($cInc.sum -gt 0) -and ($cInc.sum -lt $cAll.sum)) "inc=$($cInc.sum) all=$($cAll.sum) supId=$supId"
} else {
  Add-Result 'CHARTS: supplier with sales found' $false 'none in range'
}

# ============================================================
# C. BELOW MIN STOCK — proves the supplier-filter join
# ============================================================
function Get-Bms($selJson) {
  $fd = @{ }
  if ($selJson) { $fd['itemsSelection'] = $selJson }
  $r = Invoke-WebRequest "$base/Reports/GetBelowMinStockData" -Method Post -Body $fd -WebSession $session -UseBasicParsing -TimeoutSec 300
  $j = $r.Content | ConvertFrom-Json
  $cnt = 0; if ($j.success -and $j.data) { $cnt = @($j.data).Count }
  return @{ ok=[bool]$j.success; count=$cnt; msg=$j.message }
}
$bAll = Get-Bms $null
Add-Result 'BMS: unfiltered runs (success)' $bAll.ok ("rows=" + $bAll.count)

# find a supplier that has below-min items
$bSupId=$null
foreach ($s in ($sups | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 120)) {
  $cnt = [int](SqlScalar "SELECT COUNT(*) FROM tbl_RelItemStore ris JOIN tbl_Item t2 ON ris.fk_ItemID=t2.pk_ItemID JOIN tbl_RelItemSuppliers rs ON t2.pk_ItemID=rs.fk_ItemID AND ISNULL(rs.PrimarySupplier,0)=1 WHERE ISNULL(ris.MinimumStock,0)>0 AND ISNULL(ris.Stock,0)<ISNULL(ris.MinimumStock,0) AND rs.fk_SupplierNo=$($s.id);")
  if ($cnt -gt 0) { $bSupId = $s.id; break }
}
if ($bSupId) {
  $bInc = Get-Bms ('{"suppliers":{"ids":["' + $bSupId + '"],"mode":"include"}}')
  Add-Result 'BMS: supplier filter runs WITHOUT SQL error (join added)' $bInc.ok ("ok=$($bInc.ok) msg=$($bInc.msg)")
  $dbBms = [int](SqlScalar "SELECT COUNT(*) FROM tbl_RelItemStore ris JOIN tbl_Item t2 ON ris.fk_ItemID=t2.pk_ItemID JOIN tbl_RelItemSuppliers rs ON t2.pk_ItemID=rs.fk_ItemID AND ISNULL(rs.PrimarySupplier,0)=1 WHERE ISNULL(ris.MinimumStock,0)>0 AND ISNULL(ris.Stock,0)<ISNULL(ris.MinimumStock,0) AND rs.fk_SupplierNo=$bSupId;")
  Add-Result 'BMS: supplier rows == DB count' ($bInc.count -eq $dbBms) "app=$($bInc.count) db=$dbBms supId=$bSupId"
  Add-Result 'BMS: supplier include <= unfiltered' ($bInc.count -le $bAll.count) "inc=$($bInc.count) all=$($bAll.count)"
} else {
  Add-Result 'BMS: supplier with below-min items found' $false 'none'
}

# ---- summary ----
Write-Host ""
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$pass = ($results | Where-Object Status -eq 'PASS').Count
$fail = ($results | Where-Object Status -eq 'FAIL').Count
Write-Host "============================================"
Write-Host "ROLLOUT-FILTER QA  TOTAL:$($results.Count)  PASS:$pass  FAIL:$fail"
Write-Host "============================================"
if ($fail -gt 0) { $results | Where-Object Status -eq 'FAIL' | ForEach-Object { Write-Host (" FAIL - " + $_.Test + " :: " + $_.Detail) } }

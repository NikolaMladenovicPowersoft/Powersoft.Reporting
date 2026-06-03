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

# ---- AB CSV export helpers (ExportCsv is the AverageBasket CSV endpoint) ----
function Get-AbCsv($selJson, $groupBy) {
  if (-not $groupBy) { $groupBy = 'None' }
  $url = "$base/Reports/ExportCsv?dateFrom=$df&dateTo=$dt&breakdown=Monthly&groupBy=$groupBy&secondaryGroupBy=None&includeVat=false&compareLastYear=false&sortColumn=Period&sortDirection=ASC"
  if ($selJson) { $url += "&ItemsSelectionJson=" + [uri]::EscapeDataString($selJson) }
  return (Invoke-WebRequest $url -WebSession $session -UseBasicParsing -TimeoutSec 240).Content
}
# returns @{ total=<grand total Net Sales>; groups=@(distinct group names) }
function Parse-Ab($content) {
  $lines = ($content -split "`r?`n") | Where-Object { $_ -and ($_ -notmatch '^\s*#') }
  $hdrIdx = -1
  for ($i=0;$i -lt $lines.Count;$i++){ if ($lines[$i] -match '(^|,)"?Period"?(,|$)'){ $hdrIdx=$i; break } }
  if ($hdrIdx -lt 0) { return @{ total=$null; groups=@() } }
  $hdr = $lines[$hdrIdx].Split(',') | ForEach-Object { $_.Trim('"').Trim() }
  $salesIdx = -1
  for ($i=0;$i -lt $hdr.Count;$i++){ if ($hdr[$i] -match 'Sales'){ $salesIdx=$i; break } }
  $hasGroup = ($hdr[0] -notmatch '^Period$')
  $total=$null; $groups=@{}
  for ($i=$hdrIdx+1;$i -lt $lines.Count;$i++){
    $c = $lines[$i].Split(',') | ForEach-Object { $_.Trim('"').Trim() }
    if ($lines[$i] -match 'GRAND TOTAL') { if ($salesIdx -ge 0 -and $c.Count -gt $salesIdx){ $total=[double]$c[$salesIdx] }; continue }
    if ($hasGroup -and $c[0] -ne '') { $groups[$c[0]] = $true }
  }
  return @{ total=$total; groups=@($groups.Keys) }
}

# ---- pick a high-volume category ----
$cats = (Invoke-WebRequest "$base/Reports/GetDimensions?type=Category" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
$cat = $cats | Where-Object { $_.id -eq '292' } | Select-Object -First 1
if (-not $cat) { $cat = $cats | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 1 }
$catId = $cat.id
Add-Result 'DATA: GetDimensions Category' ([bool]$cat) ("cat id=$catId name=$($cat.name)")

# ---- 1. THREADING: export honors the dimension filter (the core bug) ----
$all = Parse-Ab (Get-AbCsv $null 'None')
$incJson = '{"categories":{"ids":["' + $catId + '"],"mode":"include"}}'
$excJson = '{"categories":{"ids":["' + $catId + '"],"mode":"exclude"}}'
$inc = Parse-Ab (Get-AbCsv $incJson 'None')
$exc = Parse-Ab (Get-AbCsv $excJson 'None')
Add-Result 'THREAD: unfiltered total > 0' ($all.total -gt 0) "all=$($all.total)"
Add-Result 'THREAD: include reduces total' (($inc.total -gt 0) -and ($inc.total -lt $all.total)) "inc=$($inc.total) all=$($all.total)"
Add-Result 'THREAD: exclude reduces total' (($exc.total -gt 0) -and ($exc.total -lt $all.total)) "exc=$($exc.total) all=$($all.total)"
$part = [math]::Abs(($inc.total + $exc.total) - $all.total)
Add-Result 'THREAD: include+exclude == all (partition, NULL-keep)' ($part -lt 1.0) "inc+exc=$($inc.total + $exc.total) all=$($all.total) diff=$part"

# ---- 2. DB cross-check: include total == DB sales-leg net for that category ----
# App "Net Sales" total = sales leg minus returns (credit) leg, both restricted to the category.
$qCat = "DECLARE @s float=(SELECT SUM(t2.Amount - ISNULL(t2.Discount,0) - ISNULL(t2.ExtraDiscount,0)) FROM tbl_InvoiceHeader t1 JOIN tbl_InvoiceDetails t2 ON t1.pk_InvoiceID=t2.fk_Invoice JOIN tbl_Item it ON t2.fk_ItemID=it.pk_ItemID WHERE CONVERT(DATE,t1.DateTrans) BETWEEN '$df' AND '$dt' AND ISNULL(it.fk_CategoryID,-1)=$catId); DECLARE @r float=(SELECT SUM(c2.Amount - ISNULL(c2.Discount,0) - ISNULL(c2.ExtraDiscount,0)) FROM tbl_CreditHeader c1 JOIN tbl_CreditDetails c2 ON c1.pk_CreditID=c2.fk_Credit JOIN tbl_Item it ON c2.fk_ItemID=it.pk_ItemID WHERE CONVERT(DATE,c1.DateTrans) BETWEEN '$df' AND '$dt' AND ISNULL(it.fk_CategoryID,-1)=$catId); SELECT ISNULL(@s,0)-ISNULL(@r,0);"
$dbCat = [double](SqlScalar $qCat)
$diffCat = [math]::Abs($dbCat - $inc.total)
Add-Result 'DB: AB include total == DB sales-leg for category' ($diffCat -lt 1.0) "app=$($inc.total) db=$dbCat diff=$diffCat"

# ---- 3. grouping by Category + include => exactly one group row (that category) ----
$incG = Parse-Ab (Get-AbCsv $incJson 'Category')
Add-Result 'GROUP: include => exactly 1 Category group' ($incG.groups.Count -eq 1) "groups=$($incG.groups.Count) -> $($incG.groups -join '|')"

# ---- 4. SUPPLIER dimension (new in AbDimCols) ----
$sups = (Invoke-WebRequest "$base/Reports/GetDimensions?type=Supplier" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
# choose a supplier that has sales activity in range (primary supplier on a sold item)
$supId = $null
foreach ($s in ($sups | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 25)) {
  $q = "SELECT COUNT(*) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_RelItemSuppliers r ON d.fk_ItemID=r.fk_ItemID AND ISNULL(r.PrimarySupplier,0)=1 WHERE r.fk_SupplierNo='$($s.id -replace "'","''")' AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt';"
  if ([int](SqlScalar $q) -gt 0) { $supId = $s.id; break }
}
if ($supId) {
  $supJson = '{"suppliers":{"ids":["' + ($supId -replace '"','\"') + '"],"mode":"include"}}'
  $supInc = Parse-Ab (Get-AbCsv $supJson 'None')
  Add-Result 'SUPPLIER: include reduces total' (($supInc.total -gt 0) -and ($supInc.total -lt $all.total)) "sup=$($supInc.total) all=$($all.total) supId=$supId"
  $sup2 = ($supId -replace "'","''")
  $qSup = "DECLARE @s float=(SELECT SUM(d.Amount - ISNULL(d.Discount,0) - ISNULL(d.ExtraDiscount,0)) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_RelItemSuppliers r ON d.fk_ItemID=r.fk_ItemID AND ISNULL(r.PrimarySupplier,0)=1 WHERE r.fk_SupplierNo='$sup2' AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt'); DECLARE @r float=(SELECT SUM(d.Amount - ISNULL(d.Discount,0) - ISNULL(d.ExtraDiscount,0)) FROM tbl_CreditHeader h JOIN tbl_CreditDetails d ON h.pk_CreditID=d.fk_Credit JOIN tbl_RelItemSuppliers r ON d.fk_ItemID=r.fk_ItemID AND ISNULL(r.PrimarySupplier,0)=1 WHERE r.fk_SupplierNo='$sup2' AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt'); SELECT ISNULL(@s,0)-ISNULL(@r,0);"
  $dbSup = [double](SqlScalar $qSup)
  $diffSup = [math]::Abs($dbSup - $supInc.total)
  Add-Result 'DB: AB supplier include total == DB primary-supplier sales-leg' ($diffSup -lt 1.0) "app=$($supInc.total) db=$dbSup diff=$diffSup"
  $supG = Parse-Ab (Get-AbCsv $supJson 'Supplier')
  Add-Result 'SUPPLIER: group by Supplier + include => exactly 1 group' ($supG.groups.Count -eq 1) "groups=$($supG.groups.Count) -> $($supG.groups -join '|')"
} else {
  Add-Result 'SUPPLIER: found supplier with activity' $false 'no primary-supplier sales in range (cannot verify)'
}

# ---- summary ----
Write-Host ""
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$pass = ($results | Where-Object Status -eq 'PASS').Count
$fail = ($results | Where-Object Status -eq 'FAIL').Count
Write-Host "============================================"
Write-Host "AVERAGE-BASKET QA  TOTAL:$($results.Count)  PASS:$pass  FAIL:$fail"
Write-Host "============================================"
if ($fail -gt 0) { $results | Where-Object Status -eq 'FAIL' | ForEach-Object { Write-Host (" FAIL - " + $_.Test + " :: " + $_.Detail) } }

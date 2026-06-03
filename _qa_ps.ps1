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

# ---- PS CSV export helper (ExportPsCsv is [HttpGet,HttpPost]; Detailed mode, no VAT) ----
function Get-PsCsv($selJson, $primaryGroup) {
  if (-not $primaryGroup) { $primaryGroup = 'None' }
  $url = "$base/Reports/ExportPsCsv?dateFrom=$df&dateTo=$dt&reportMode=Detailed&primaryGroup=$primaryGroup&secondaryGroup=None&thirdGroup=None&includeVat=false&showProfit=false&showStock=false&sortColumn=ItemCode&sortDirection=ASC&showOnOrder=false&showReservation=false&showAvailable=false&includeAdditionalCharges=true"
  if ($selJson) { $url += "&ItemsSelectionJson=" + [uri]::EscapeDataString($selJson) }
  return (Invoke-WebRequest $url -WebSession $session -UseBasicParsing -TimeoutSec 300).Content
}
# returns @{ netSold=<grand total Net Sold>; groups=@(distinct group labels) }
function Parse-Ps($content) {
  $lines = ($content -split "`r?`n") | Where-Object { $_ -and ($_ -notmatch '^\s*#') }
  $hdrIdx = -1
  for ($i=0;$i -lt $lines.Count;$i++){ if ($lines[$i] -match 'Qty Purchased'){ $hdrIdx=$i; break } }
  if ($hdrIdx -lt 0) { return @{ netSold=$null; groups=@() } }
  $hdr = $lines[$hdrIdx].Split(',') | ForEach-Object { $_.Trim('"').Trim() }
  $soldIdx = -1
  for ($i=0;$i -lt $hdr.Count;$i++){ if ($hdr[$i] -match 'Net Sold'){ $soldIdx=$i; break } }
  $hasGroup = ($hdr[0] -notmatch '^Item Code$' -and $hdr[0] -notmatch '^Qty Purchased$')
  $netSold=$null; $groups=@{}
  for ($i=$hdrIdx+1;$i -lt $lines.Count;$i++){
    $c = $lines[$i].Split(',') | ForEach-Object { $_.Trim('"').Trim() }
    if ($lines[$i] -match 'Subtotal') { continue }
    if (($c -contains 'TOTAL')) { if ($soldIdx -ge 0 -and $c.Count -gt $soldIdx){ $netSold=[double]$c[$soldIdx] }; continue }
    if ($hasGroup -and $c[0] -ne '') { $groups[$c[0]] = $true }
  }
  return @{ netSold=$netSold; groups=@($groups.Keys) }
}

# net-sold leg = invoices net minus credits net, restricted by an extra item-join predicate
function DbNetSold($joinPredicate) {
  $q = "DECLARE @s float=(SELECT SUM(d.Amount - ISNULL(d.Discount,0) - ISNULL(d.ExtraDiscount,0)) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_Item it ON d.fk_ItemID=it.pk_ItemID $joinPredicate); DECLARE @r float=(SELECT SUM(d.Amount - ISNULL(d.Discount,0) - ISNULL(d.ExtraDiscount,0)) FROM tbl_CreditHeader h JOIN tbl_CreditDetails d ON h.pk_CreditID=d.fk_Credit JOIN tbl_Item it ON d.fk_ItemID=it.pk_ItemID $joinPredicate); SELECT ISNULL(@s,0)-ISNULL(@r,0);"
  return [double](SqlScalar $q)
}

$all = Parse-Ps (Get-PsCsv $null 'None')
Add-Result 'BASE: unfiltered Net Sold > 0' ($all.netSold -gt 0) "all=$($all.netSold)"

# ===== 1. MODEL dimension (t2.fk_ModelID) =====
$models = (Invoke-WebRequest "$base/Reports/GetDimensions?type=Model" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
$modelId = $null
foreach ($m in ($models | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 60)) {
  $cnt = [int](SqlScalar "SELECT COUNT(*) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_Item it ON d.fk_ItemID=it.pk_ItemID WHERE it.fk_ModelID=$($m.id) AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt';")
  if ($cnt -gt 0) { $modelId = $m.id; break }
}
if ($modelId) {
  $incJson = '{"models":{"ids":["' + $modelId + '"],"mode":"include"}}'
  $excJson = '{"models":{"ids":["' + $modelId + '"],"mode":"exclude"}}'
  $inc = Parse-Ps (Get-PsCsv $incJson 'None')
  $exc = Parse-Ps (Get-PsCsv $excJson 'None')
  Add-Result 'MODEL: include reduces Net Sold' (($inc.netSold -gt 0) -and ($inc.netSold -lt $all.netSold)) "inc=$($inc.netSold) all=$($all.netSold) modelId=$modelId"
  $part = [math]::Abs(($inc.netSold + $exc.netSold) - $all.netSold)
  Add-Result 'MODEL: include+exclude == all (partition, NULL-keep)' ($part -lt 1.0) "inc+exc=$($inc.netSold + $exc.netSold) all=$($all.netSold) diff=$part"
  $dbModel = DbNetSold "WHERE it.fk_ModelID=$modelId AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt'"
  $diff = [math]::Abs($dbModel - $inc.netSold)
  Add-Result 'DB: MODEL include Net Sold == DB sales-leg' ($diff -lt 1.0) "app=$($inc.netSold) db=$dbModel diff=$diff"
  $incG = Parse-Ps (Get-PsCsv $incJson 'Model')
  Add-Result 'MODEL: group by Model + include => exactly 1 group' ($incG.groups.Count -eq 1) "groups=$($incG.groups.Count) -> $($incG.groups -join '|')"
} else {
  Add-Result 'MODEL: found model with sales activity' $false 'no model sales in range (cannot verify)'
}

# ===== 2. FABRIC dimension (t5.fk_FabricID via tbl_Model join) =====
$fabrics = (Invoke-WebRequest "$base/Reports/GetDimensions?type=Fabric" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
$fabricId = $null
foreach ($f in ($fabrics | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 60)) {
  $cnt = [int](SqlScalar "SELECT COUNT(*) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_Item it ON d.fk_ItemID=it.pk_ItemID JOIN tbl_Model m ON it.fk_ModelID=m.pk_ModelID WHERE m.fk_FabricID=$($f.id) AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt';")
  if ($cnt -gt 0) { $fabricId = $f.id; break }
}
if ($fabricId) {
  $fJson = '{"fabrics":{"ids":["' + $fabricId + '"],"mode":"include"}}'
  $fInc = Parse-Ps (Get-PsCsv $fJson 'None')
  Add-Result 'FABRIC: include reduces Net Sold' (($fInc.netSold -gt 0) -and ($fInc.netSold -lt $all.netSold)) "inc=$($fInc.netSold) all=$($all.netSold) fabricId=$fabricId"
  $dbFab = DbNetSold "JOIN tbl_Model m ON it.fk_ModelID=m.pk_ModelID WHERE m.fk_FabricID=$fabricId AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt'"
  $diffF = [math]::Abs($dbFab - $fInc.netSold)
  Add-Result 'DB: FABRIC include Net Sold == DB sales-leg (t5 join proven)' ($diffF -lt 1.0) "app=$($fInc.netSold) db=$dbFab diff=$diffF"
} else {
  Add-Result 'FABRIC: found fabric with sales activity' $false 'no fabric sales in range (cannot verify)'
}

# ===== 3. GROUP SIZE dimension (t5.fk_SizeGroupID, new in psDimCols) =====
$gsizes = (Invoke-WebRequest "$base/Reports/GetDimensions?type=GroupSize" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
$gsId = $null
foreach ($g in ($gsizes | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 60)) {
  $cnt = [int](SqlScalar "SELECT COUNT(*) FROM tbl_InvoiceHeader h JOIN tbl_InvoiceDetails d ON h.pk_InvoiceID=d.fk_Invoice JOIN tbl_Item it ON d.fk_ItemID=it.pk_ItemID JOIN tbl_Model m ON it.fk_ModelID=m.pk_ModelID WHERE m.fk_SizeGroupID=$($g.id) AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt';")
  if ($cnt -gt 0) { $gsId = $g.id; break }
}
if ($gsId) {
  $gJson = '{"groupSizes":{"ids":["' + $gsId + '"],"mode":"include"}}'
  $gInc = Parse-Ps (Get-PsCsv $gJson 'None')
  Add-Result 'GROUPSIZE: include reduces Net Sold' (($gInc.netSold -gt 0) -and ($gInc.netSold -lt $all.netSold)) "inc=$($gInc.netSold) all=$($all.netSold) gsId=$gsId"
  $dbGs = DbNetSold "JOIN tbl_Model m ON it.fk_ModelID=m.pk_ModelID WHERE m.fk_SizeGroupID=$gsId AND CONVERT(DATE,h.DateTrans) BETWEEN '$df' AND '$dt'"
  $diffG = [math]::Abs($dbGs - $gInc.netSold)
  Add-Result 'DB: GROUPSIZE include Net Sold == DB sales-leg (new mapping proven)' ($diffG -lt 1.0) "app=$($gInc.netSold) db=$dbGs diff=$diffG"
} else {
  Add-Result 'GROUPSIZE: found group-size with sales activity' $false 'no group-size sales in range (cannot verify)'
}

# ---- summary ----
Write-Host ""
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$pass = ($results | Where-Object Status -eq 'PASS').Count
$fail = ($results | Where-Object Status -eq 'FAIL').Count
Write-Host "============================================"
Write-Host "PURCHASES-VS-SALES FASHION QA  TOTAL:$($results.Count)  PASS:$pass  FAIL:$fail"
Write-Host "============================================"
if ($fail -gt 0) { $results | Where-Object Status -eq 'FAIL' | ForEach-Object { Write-Host (" FAIL - " + $_.Test + " :: " + $_.Detail) } }

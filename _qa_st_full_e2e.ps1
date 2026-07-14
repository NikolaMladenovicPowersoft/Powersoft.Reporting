# ============================================================================
# Sales Through — FULL E2E QA harness
# - Independent SQL verification against tenant DB (exact match)
# - All grouping dimensions, 1/2/3 levels, summary vs detail
# - Items Selection include/exclude/multi, store multi-select, customer (sale-leg)
# - Edge cases (validation, paging, sorting, malformed input)
# - Exports (CSV exact numbers, Excel/PDF magic), print preview totals
# - Schedule CRUD, Save Layout roundtrip, Send Email validation, AI analyze
# ============================================================================
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5150'
$dbConn = 'Data Source=ANDREASPS\SQLDEVELOPER17PS;Initial Catalog=pswaDEMO365MODAPRO1;User ID=sa;Password=SQLADMIN123!;TrustServerCertificate=True;Encrypt=False'
$DF = '2024-01-01'; $DT = '2024-12-31'

$script:pass = 0; $script:fail = 0; $script:failures = @()
function Check([string]$name, [bool]$cond, [string]$detail = '') {
    if ($cond) { $script:pass++; Write-Output ("  PASS  {0}" -f $name) }
    else { $script:fail++; $script:failures += "$name | $detail"; Write-Output ("  FAIL  {0}  << {1}" -f $name, $detail) }
}
function NearEq($a, $b, $tol = 0.005) { return [math]::Abs([double]$a - [double]$b) -le $tol }

# ---------- DB helper ----------
function DbQuery([string]$sql) {
    $cn = New-Object System.Data.SqlClient.SqlConnection($dbConn)
    $cn.Open()
    try {
        $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 120
        $da = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
        $dt = New-Object System.Data.DataTable
        [void]$da.Fill($dt)
        return ,$dt
    } finally { $cn.Close() }
}

# Independent 4-leg totals SQL (mirrors legacy Purchases&Sales semantics).
# $itemWhere applies to all legs (item-level dims), $saleWhere only to sale legs,
# $withCost toggles the additional-charges term on the purchase-invoice leg.
function LegsSql([string]$itemWhere = '', [string]$saleWhere = '', [bool]$withCost = $true) {
    $costTerm = ''
    $costJoin = ''
    if ($withCost) {
        $costTerm = ' + (ISNULL(c.CostAmount,0)*ISNULL(c.Quantity,0))'
        $costJoin = 'LEFT JOIN tbl_CostingDetails c ON t1.pk_ID = c.fk_ID'
    }
    return @"
SELECT t2.ItemCode,
       t1.Quantity AS pq,
       t1.Amount - (ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))$costTerm AS pv,
       CAST(0 AS DECIMAL(18,4)) sq, CAST(0 AS DECIMAL(18,4)) sn, CAST(0 AS DECIMAL(18,4)) sg
FROM tbl_PurchInvoiceDetails t1
INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID
INNER JOIN tbl_PurchInvoiceHeader t3 ON t1.fk_PurchInvoiceID = t3.pk_PurchInvoiceID
$costJoin
WHERE CONVERT(DATE, t3.DateTrans) BETWEEN '$DF' AND '$DT' $itemWhere
UNION ALL
SELECT t2.ItemCode,
       -t1.Quantity,
       -(t1.Amount - (ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))),
       0, 0, 0
FROM tbl_PurchReturnDetails t1
INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID
INNER JOIN tbl_PurchReturnHeader t3 ON t1.fk_PurchReturnID = t3.pk_PurchReturnID
WHERE CONVERT(DATE, t3.DateTrans) BETWEEN '$DF' AND '$DT' $itemWhere
UNION ALL
SELECT t2.ItemCode, 0, 0,
       t1.Quantity,
       t1.Amount - (ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0)),
       t1.Amount - (ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0)) + ISNULL(t1.VatAmount,0)
FROM tbl_InvoiceDetails t1
INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID
INNER JOIN tbl_InvoiceHeader t3 ON t1.fk_Invoice = t3.pk_InvoiceID
WHERE CONVERT(DATE, t3.DateTrans) BETWEEN '$DF' AND '$DT' $itemWhere $saleWhere
UNION ALL
SELECT t2.ItemCode, 0, 0,
       -t1.Quantity,
       -(t1.Amount - (ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0))),
       -(t1.Amount - (ISNULL(t1.Discount,0)+ISNULL(t1.ExtraDiscount,0)) + ISNULL(t1.VatAmount,0))
FROM tbl_CreditDetails t1
INNER JOIN tbl_Item t2 ON t1.fk_ItemID = t2.pk_ItemID
INNER JOIN tbl_CreditHeader t3 ON t1.fk_Credit = t3.pk_CreditID
WHERE CONVERT(DATE, t3.DateTrans) BETWEEN '$DF' AND '$DT' $itemWhere $saleWhere
"@
}

function DbTotals([string]$itemWhere = '', [string]$saleWhere = '', [bool]$withCost = $true) {
    $legs = LegsSql $itemWhere $saleWhere $withCost
    $sql = @"
;WITH legs AS ($legs)
SELECT ISNULL(SUM(pq),0) pq, ISNULL(SUM(pv),0) pv, ISNULL(SUM(sq),0) sq,
       ISNULL(SUM(sn),0) sn, ISNULL(SUM(sg),0) sg,
       ISNULL((SELECT SUM(it.TotalStockQty) FROM tbl_Item it
               WHERE it.ItemCode IN (SELECT DISTINCT ItemCode FROM legs)),0) stk
FROM legs
"@
    return (DbQuery $sql).Rows[0]
}

# ---------- HTTP helpers ----------
$session = $null
function StCall([hashtable]$extra) {
    $p = @{ dateFrom=$DF; dateTo=$DT; summary='false'; primaryGroup='None'; secondaryGroup='None'; thirdGroup='None';
            includeAdditionalCharges='true'; sortBySizeSequence='false'; storeCodes=''; itemsSelection='';
            sortColumn='ItemCode'; sortDirection='ASC'; pageNumber='1'; pageSize='100000' }
    foreach ($k in $extra.Keys) { $p[$k] = $extra[$k] }
    $r = Invoke-WebRequest "$base/Reports/GetSalesThroughData" -Method Post -Body $p -WebSession $session -UseBasicParsing -TimeoutSec 180
    return $r.Content | ConvertFrom-Json
}
function StQs([hashtable]$extra) {
    $p = [ordered]@{ dateFrom=$DF; dateTo=$DT; summary='false'; primaryGroup='None'; secondaryGroup='None'; thirdGroup='None';
            includeAdditionalCharges='true'; sortBySizeSequence='false'; storeCodes=''; itemsSelectionJson='';
            sortColumn='ItemCode'; sortDirection='ASC' }
    foreach ($k in $extra.Keys) { $p[$k] = $extra[$k] }
    return (($p.GetEnumerator() | ForEach-Object { "$($_.Key)=$([uri]::EscapeDataString([string]$_.Value))" }) -join '&')
}

# ============================================================================
Write-Output '=== 0. LOGIN + CONNECT ==='
$login = Invoke-WebRequest "$base/Account/Login" -SessionVariable session -UseBasicParsing -TimeoutSec 60
$token = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
try { Invoke-WebRequest "$base/Account/Login" -Method Post -Body @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token } -WebSession $session -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 30 | Out-Null } catch {}
$conn = (Invoke-WebRequest "$base/Home/Connect" -Method Post -Body @{ databaseCode='DEMO365MODAPRO1' } -WebSession $session -UseBasicParsing -TimeoutSec 90).Content | ConvertFrom-Json
Check 'Login + tenant connect' ($conn.success -eq $true) ($conn | ConvertTo-Json -Compress)

Write-Output ''
Write-Output '=== 1. DATA ANOMALY PRE-CHECKS (join duplication risks) ==='
$dupCost = (DbQuery 'SELECT COUNT(*) c FROM (SELECT fk_ID FROM tbl_CostingDetails GROUP BY fk_ID HAVING COUNT(*)>1) x').Rows[0].c
Check 'tbl_CostingDetails: no duplicate fk_ID (LEFT JOIN cannot duplicate rows)' ($dupCost -eq 0) "dup groups=$dupCost"
$dupSup = (DbQuery 'SELECT COUNT(*) c FROM (SELECT fk_ItemID FROM tbl_RelItemSuppliers WHERE ISNULL(PrimarySupplier,0)=1 GROUP BY fk_ItemID HAVING COUNT(*)>1) x').Rows[0].c
Check 'tbl_RelItemSuppliers: max 1 primary supplier per item' ($dupSup -eq 0) "dup items=$dupSup"
$dupItem = (DbQuery 'SELECT COUNT(*) c FROM (SELECT ItemCode FROM tbl_Item GROUP BY ItemCode HAVING COUNT(*)>1) x').Rows[0].c
Check 'tbl_Item: ItemCode unique (outer join on ItemCode safe)' ($dupItem -eq 0) "dup codes=$dupItem"

Write-Output ''
Write-Output '=== 2. TOTALS — EXACT DB MATCH ==='
$exp = DbTotals
$d = StCall @{}
Check 'Baseline: endpoint success' ($d.success -eq $true) $d.message
Check 'Baseline: TotalIntakeQty == DB'   (NearEq $d.totals.totalIntakeQty   $exp.pq) "app=$($d.totals.totalIntakeQty) db=$($exp.pq)"
Check 'Baseline: TotalIntakeValue == DB' (NearEq $d.totals.totalIntakeValue $exp.pv) "app=$($d.totals.totalIntakeValue) db=$($exp.pv)"
Check 'Baseline: TotalSalesQty == DB'    (NearEq $d.totals.totalSalesQty    $exp.sq) "app=$($d.totals.totalSalesQty) db=$($exp.sq)"
Check 'Baseline: TotalSalesNet == DB'    (NearEq $d.totals.totalSalesNet    $exp.sn) "app=$($d.totals.totalSalesNet) db=$($exp.sn)"
Check 'Baseline: TotalSalesGross == DB'  (NearEq $d.totals.totalSalesGross  $exp.sg) "app=$($d.totals.totalSalesGross) db=$($exp.sg)"
Check 'Baseline: TotalCurrentStock == DB'(NearEq $d.totals.totalCurrentStock $exp.stk) "app=$($d.totals.totalCurrentStock) db=$($exp.stk)"

# rows vs totals consistency (all rows fetched)
$sumIQ = ($d.data | Measure-Object -Property intakeQty -Sum).Sum
$sumIV = ($d.data | Measure-Object -Property intakeValue -Sum).Sum
$sumSQ = ($d.data | Measure-Object -Property salesQty -Sum).Sum
$sumSN = ($d.data | Measure-Object -Property salesNet -Sum).Sum
$sumST = ($d.data | Measure-Object -Property currentStock -Sum).Sum
Check 'Baseline: sum(rows.intakeQty) == totals'  (NearEq $sumIQ $d.totals.totalIntakeQty) "rows=$sumIQ tot=$($d.totals.totalIntakeQty)"
Check 'Baseline: sum(rows.intakeValue) == totals'(NearEq $sumIV $d.totals.totalIntakeValue) "rows=$sumIV tot=$($d.totals.totalIntakeValue)"
Check 'Baseline: sum(rows.salesQty) == totals'   (NearEq $sumSQ $d.totals.totalSalesQty) "rows=$sumSQ tot=$($d.totals.totalSalesQty)"
Check 'Baseline: sum(rows.salesNet) == totals'   (NearEq $sumSN $d.totals.totalSalesNet) "rows=$sumSN tot=$($d.totals.totalSalesNet)"
Check 'Baseline: sum(rows.currentStock) == totals' (NearEq $sumST $d.totals.totalCurrentStock) "rows=$sumST tot=$($d.totals.totalCurrentStock)"
Check 'Baseline: totalRows == data.Count (all rows fetch)' ($d.totalRows -eq $d.data.Count) "totalRows=$($d.totalRows) rows=$($d.data.Count)"

# Mix % re-derivation
$r0 = $d.data | Where-Object { $_.salesQty -ne 0 } | Select-Object -First 1
if ($r0) {
    $expMix = [math]::Round($r0.salesQty / $d.totals.totalSalesQty * 100, 2)
    Check 'Baseline: SalesMixPct = round(rowQty/totalQty*100,2)' (NearEq $r0.salesMixPct $expMix 0.011) "app=$($r0.salesMixPct) exp=$expMix"
}
# Sell-through zero-intake convention
$rz = $d.data | Where-Object { $_.intakeQty -eq 0 } | Select-Object -First 1
if ($rz) { Check 'Zero intake row -> SellThroughQtyPct = 100 (workbook convention)' ($rz.sellThroughQtyPct -eq 100) "got=$($rz.sellThroughQtyPct)" }

Write-Output ''
Write-Output '=== 3. WHOLESALE-ONLY COST (includeAdditionalCharges=false) ==='
$expW = DbTotals '' '' $false
$dw = StCall @{ includeAdditionalCharges='false' }
Check 'Wholesale: TotalIntakeValue == DB(no cost term)' (NearEq $dw.totals.totalIntakeValue $expW.pv) "app=$($dw.totals.totalIntakeValue) db=$($expW.pv)"
Check 'Wholesale: sales unchanged vs baseline' (NearEq $dw.totals.totalSalesNet $d.totals.totalSalesNet) ''

Write-Output ''
Write-Output '=== 4. ROW-LEVEL EXACT MATCH (3 sample items) ==='
$legs = LegsSql
$perItem = DbQuery @"
;WITH legs AS ($legs)
SELECT TOP 3 l.ItemCode, SUM(pq) pq, SUM(pv) pv, SUM(sq) sq, SUM(sn) sn, SUM(sg) sg
FROM legs l GROUP BY l.ItemCode
HAVING SUM(ABS(pq)) + SUM(ABS(sq)) > 0
ORDER BY SUM(ABS(sn)) DESC
"@
foreach ($row in $perItem.Rows) {
    $appRow = $d.data | Where-Object { $_.itemCode -eq $row.ItemCode } | Select-Object -First 1
    if ($null -eq $appRow) { Check "Item $($row.ItemCode): present in report" $false 'missing row'; continue }
    $ok = (NearEq $appRow.intakeQty $row.pq) -and (NearEq $appRow.intakeValue $row.pv) -and
          (NearEq $appRow.salesQty $row.sq) -and (NearEq $appRow.salesNet $row.sn) -and (NearEq $appRow.salesGross $row.sg)
    Check "Item $($row.ItemCode): qty/value/sales exact match" $ok "app=($($appRow.intakeQty),$($appRow.intakeValue),$($appRow.salesQty),$($appRow.salesNet)) db=($($row.pq),$($row.pv),$($row.sq),$($row.sn))"
}
# sales-only item must appear (union legs, not INNER purchase join)
$soRow = DbQuery @"
;WITH legs AS ($legs)
SELECT TOP 1 ItemCode FROM legs GROUP BY ItemCode HAVING SUM(ABS(pq))=0 AND SUM(ABS(sq))>0
"@
if ($soRow.Rows.Count -gt 0) {
    $soCode = $soRow.Rows[0].ItemCode
    $appSo = $d.data | Where-Object { $_.itemCode -eq $soCode } | Select-Object -First 1
    Check "Sales-only item $soCode appears with intake 0" ($null -ne $appSo -and $appSo.intakeQty -eq 0) ''
}

Write-Output ''
Write-Output '=== 5. ITEMS SELECTION — include/exclude/multi + DB MATCH ==='
$cats = DbQuery "SELECT TOP 2 pk_CategoryID id FROM tbl_ItemCategory ORDER BY pk_CategoryID"
if ($cats.Rows.Count -ge 2) {
    $c1 = $cats.Rows[0].id; $c2 = $cats.Rows[1].id
    # include 2 categories
    $selJson = '{"categories":{"ids":["' + $c1 + '","' + $c2 + '"],"mode":"include"}}'
    $di = StCall @{ itemsSelection=$selJson }
    $expI = DbTotals " AND t2.fk_CategoryID IN ($c1,$c2)"
    Check 'Cat include x2: IntakeValue == DB' (NearEq $di.totals.totalIntakeValue $expI.pv) "app=$($di.totals.totalIntakeValue) db=$($expI.pv)"
    Check 'Cat include x2: SalesNet == DB'    (NearEq $di.totals.totalSalesNet    $expI.sn) "app=$($di.totals.totalSalesNet) db=$($expI.sn)"
    # exclude same 2 categories (NULL category rows must be KEPT)
    $selJson = '{"categories":{"ids":["' + $c1 + '","' + $c2 + '"],"mode":"exclude"}}'
    $de = StCall @{ itemsSelection=$selJson }
    $expE = DbTotals " AND (t2.fk_CategoryID NOT IN ($c1,$c2) OR t2.fk_CategoryID IS NULL)"
    Check 'Cat exclude x2: IntakeValue == DB (NULLs kept)' (NearEq $de.totals.totalIntakeValue $expE.pv) "app=$($de.totals.totalIntakeValue) db=$($expE.pv)"
    Check 'Cat exclude x2: SalesNet == DB'    (NearEq $de.totals.totalSalesNet $expE.sn) "app=$($de.totals.totalSalesNet) db=$($expE.sn)"
    # include + exclude partition the baseline exactly
    Check 'Include + Exclude == Baseline (SalesNet partition)' (NearEq ($di.totals.totalSalesNet + $de.totals.totalSalesNet) $d.totals.totalSalesNet) "inc=$($di.totals.totalSalesNet) exc=$($de.totals.totalSalesNet) base=$($d.totals.totalSalesNet)"
}
# brand + season combined include
$br = DbQuery "SELECT TOP 1 pk_BrandID id FROM tbl_Brands ORDER BY pk_BrandID"
$se = DbQuery "SELECT TOP 1 pk_SeasonID id FROM tbl_Season ORDER BY pk_SeasonID"
if ($br.Rows.Count -ge 1 -and $se.Rows.Count -ge 1) {
    $b1 = $br.Rows[0].id; $s1 = $se.Rows[0].id
    $selJson = '{"brands":{"ids":["' + $b1 + '"],"mode":"include"},"seasons":{"ids":["' + $s1 + '"],"mode":"include"}}'
    $dbs = StCall @{ itemsSelection=$selJson }
    $expBS = DbTotals " AND t2.fk_BrandID IN ($b1) AND t2.fk_SeasonID IN ($s1)"
    Check 'Brand+Season include: SalesNet == DB' (NearEq $dbs.totals.totalSalesNet $expBS.sn) "app=$($dbs.totals.totalSalesNet) db=$($expBS.sn)"
    Check 'Brand+Season include: IntakeValue == DB' (NearEq $dbs.totals.totalIntakeValue $expBS.pv) "app=$($dbs.totals.totalIntakeValue) db=$($expBS.pv)"
}
# specific items include (multi)
$its = DbQuery @"
;WITH legs AS ($legs)
SELECT TOP 2 t2.pk_ItemID id FROM legs l INNER JOIN tbl_Item t2 ON l.ItemCode=t2.ItemCode
GROUP BY t2.pk_ItemID ORDER BY SUM(ABS(l.sn)) DESC
"@
if ($its.Rows.Count -ge 2) {
    $i1 = $its.Rows[0].id; $i2 = $its.Rows[1].id
    $selJson = '{"items":{"ids":["' + $i1 + '","' + $i2 + '"],"mode":"include"}}'
    $dit = StCall @{ itemsSelection=$selJson }
    $expIt = DbTotals " AND t1.fk_ItemID IN ($i1,$i2)"
    Check 'Items include x2: SalesNet == DB' (NearEq $dit.totals.totalSalesNet $expIt.sn) "app=$($dit.totals.totalSalesNet) db=$($expIt.sn)"
    Check 'Items include x2: row count <= 2' ($dit.data.Count -le 2) "rows=$($dit.data.Count)"
}
# store multi-select: legacy param vs itemsSelection must agree + DB match
$stores = DbQuery "SELECT DISTINCT TOP 2 fk_StoreCode c FROM tbl_InvoiceHeader WHERE fk_StoreCode IS NOT NULL ORDER BY fk_StoreCode"
if ($stores.Rows.Count -ge 1) {
    $scList = ($stores.Rows | ForEach-Object { $_.c })
    $scCsv = $scList -join ','
    $scSqlIn = ($scList | ForEach-Object { "'" + $_ + "'" }) -join ','
    $ds1 = StCall @{ storeCodes=$scCsv }
    $selJson = '{"stores":{"ids":[' + (($scList | ForEach-Object { '"' + $_ + '"' }) -join ',') + '],"mode":"include"}}'
    $ds2 = StCall @{ itemsSelection=$selJson }
    $expS = DbTotals " AND t3.fk_StoreCode IN ($scSqlIn)"
    Check "Store multi ($scCsv) legacy param: SalesNet == DB" (NearEq $ds1.totals.totalSalesNet $expS.sn) "app=$($ds1.totals.totalSalesNet) db=$($expS.sn)"
    Check 'Store multi: legacy param == itemsSelection stores' (NearEq $ds1.totals.totalSalesNet $ds2.totals.totalSalesNet) "legacy=$($ds1.totals.totalSalesNet) sel=$($ds2.totals.totalSalesNet)"
    Check 'Store multi: intake also filtered by store' (NearEq $ds1.totals.totalIntakeValue $expS.pv) "app=$($ds1.totals.totalIntakeValue) db=$($expS.pv)"
}
# customer filter — SALE LEG ONLY (intake must stay = baseline)
$cust = DbQuery "SELECT TOP 1 h.fk_CustomerCode c FROM tbl_InvoiceHeader h WHERE h.fk_CustomerCode IS NOT NULL AND CONVERT(DATE,h.DateTrans) BETWEEN '$DF' AND '$DT' GROUP BY h.fk_CustomerCode ORDER BY COUNT(*) DESC"
if ($cust.Rows.Count -ge 1) {
    $cc = $cust.Rows[0].c
    $selJson = '{"customers":{"ids":["' + $cc + '"],"mode":"include"}}'
    $dc = StCall @{ itemsSelection=$selJson }
    $expC = DbTotals '' " AND t3.fk_CustomerCode IN ('$cc')"
    Check "Customer $cc include: SalesNet == DB (sale-leg only)" (NearEq $dc.totals.totalSalesNet $expC.sn) "app=$($dc.totals.totalSalesNet) db=$($expC.sn)"
    Check 'Customer include: IntakeValue == baseline (purchases untouched)' (NearEq $dc.totals.totalIntakeValue $d.totals.totalIntakeValue) "app=$($dc.totals.totalIntakeValue) base=$($d.totals.totalIntakeValue)"
}

Write-Output ''
Write-Output '=== 6. GROUPING — ALL 12 DIMENSIONS ==='
$allGroups = @('Category','Department','Brand','Season','Supplier','Store','Model','Colour','Size','GroupSize','Fabric','Franchise')
foreach ($g in $allGroups) {
    $dg = StCall @{ primaryGroup=$g; summary='true' }
    if ($dg.success -ne $true) { Check "Group=$g summary: success" $false $dg.message; continue }
    $gSumSN = ($dg.data | Measure-Object -Property salesNet -Sum).Sum
    $gSumIV = ($dg.data | Measure-Object -Property intakeValue -Sum).Sum
    $okTot = (NearEq $gSumSN $d.totals.totalSalesNet) -and (NearEq $gSumIV $d.totals.totalIntakeValue)
    $nullL1 = @($dg.data | Where-Object { [string]::IsNullOrEmpty($_.level1Value) }).Count
    Check "Group=$g summary: sum(groups)==grand total + level1Value set" ($okTot -and $nullL1 -eq 0) "sumSN=$gSumSN totSN=$($d.totals.totalSalesNet) sumIV=$gSumIV totIV=$($d.totals.totalIntakeValue) nullL1=$nullL1"
}
# store-grouped stock semantics (known PS-parity nuance): report only
$dst = StCall @{ primaryGroup='Store'; summary='true' }
$stSum = ($dst.data | Measure-Object -Property currentStock -Sum).Sum
Write-Output ("  INFO  Store grouping: sum(row stock)={0} vs totals stock={1} (per-store stock vs company total - PS parity)" -f $stSum, $dst.totals.totalCurrentStock)

Write-Output ''
Write-Output '=== 7. 3-LEVEL NESTING — DETAIL vs SUMMARY CONSISTENCY ==='
$d3 = StCall @{ primaryGroup='Category'; secondaryGroup='Brand'; thirdGroup='Season' }
$s3 = StCall @{ primaryGroup='Category'; secondaryGroup='Brand'; thirdGroup='Season'; summary='true' }
Check '3-level detail: success' ($d3.success -eq $true) $d3.message
Check '3-level summary: success' ($s3.success -eq $true) $s3.message
$comboD = @($d3.data | ForEach-Object { "$($_.level1Value)|$($_.level2Value)|$($_.level3Value)" } | Sort-Object -Unique)
$comboS = @($s3.data | ForEach-Object { "$($_.level1Value)|$($_.level2Value)|$($_.level3Value)" } | Sort-Object -Unique)
Check '3-level: summary rows == distinct detail combos' ($comboS.Count -eq $s3.data.Count -and $comboD.Count -eq $comboS.Count) "detailCombos=$($comboD.Count) summaryRows=$($s3.data.Count)"
# ordering: rows must arrive sorted L1,L2,L3 (nested rendering relies on adjacency)
$sorted = $true; $prev = ''
foreach ($r in $d3.data) {
    $key = "$($r.level1Value)|$($r.level2Value)|$($r.level3Value)"
    if ($key -lt $prev -and $prev -ne '') { } # string compare only within same L1/L2 is meaningful; do exact group adjacency test below
    $prev = $key
}
# adjacency test: each combo appears as ONE contiguous block
$seen = @{}; $adjacent = $true; $prevKey = $null
foreach ($r in $d3.data) {
    $key = "$($r.level1Value)|$($r.level2Value)|$($r.level3Value)"
    if ($key -ne $prevKey) {
        if ($seen.ContainsKey($key)) { $adjacent = $false; break }
        $seen[$key] = $true; $prevKey = $key
    }
}
Check '3-level: group combos contiguous (nested UI grouping safe)' $adjacent 'combo re-appeared after break'
# per-L1 subtotal vs summary-by-L1
$s1 = StCall @{ primaryGroup='Category'; summary='true' }
$firstCat = $s1.data[0]
$detCat = $d3.data | Where-Object { $_.level1Value -eq $firstCat.level1Value }
$detSum = ($detCat | Measure-Object -Property salesNet -Sum).Sum
Check "L1 subtotal ($($firstCat.level1Value)): detail sum == summary row" (NearEq $detSum $firstCat.salesNet) "detail=$detSum summary=$($firstCat.salesNet)"

Write-Output ''
Write-Output '=== 8. EDGE CASES ==='
$e1 = StCall @{ dateFrom='2024-12-31'; dateTo='2024-01-01' }
Check 'dateFrom > dateTo -> validation error' ($e1.success -eq $false -and $e1.message -match 'Date From') ($e1 | ConvertTo-Json -Compress)
$e2 = StCall @{ dateFrom='2020-01-01'; dateTo='2024-12-31' }
Check 'range > 3 years -> validation error' ($e2.success -eq $false -and $e2.message -match '3 years') ($e2 | ConvertTo-Json -Compress)
$e3 = StCall @{ dateFrom='1990-01-01'; dateTo='1990-12-31' }
Check 'empty period -> success, 0 rows, zero totals' ($e3.success -eq $true -and $e3.data.Count -eq 0 -and $e3.totals.totalSalesNet -eq 0) "rows=$($e3.data.Count)"
$e4 = StCall @{ primaryGroup='Category'; secondaryGroup='Category' }
Check 'primary == secondary group -> validation error' ($e4.success -eq $false -and $e4.message -match 'different') ($e4.message)
$e5 = StCall @{ primaryGroup='Bogus123' }
Check 'unknown group name -> treated as None (no crash)' ($e5.success -eq $true -and (NearEq $e5.totals.totalSalesNet $d.totals.totalSalesNet)) $e5.message
$e6 = StCall @{ itemsSelection='{{{not-json' }
Check 'malformed itemsSelection JSON -> ignored, baseline numbers' ($e6.success -eq $true -and (NearEq $e6.totals.totalSalesNet $d.totals.totalSalesNet)) "app=$($e6.totals.totalSalesNet)"
$e7 = StCall @{ sortColumn="ItemCode;DROP TABLE x--" }
Check 'sort column injection attempt -> whitelisted fallback, success' ($e7.success -eq $true -and (NearEq $e7.totals.totalSalesNet $d.totals.totalSalesNet)) $e7.message
$e8 = StCall @{ sortColumn='SalesNet'; sortDirection='DESC'; pageSize='50' }
$vals = @($e8.data | ForEach-Object { [double]$_.salesNet })
$desc = $true; for ($i=1; $i -lt $vals.Count; $i++) { if ($vals[$i] -gt $vals[$i-1] + 0.001) { $desc = $false; break } }
Check 'sort SalesNet DESC: page ordered non-increasing' ($e8.success -eq $true -and $desc) ''
$p1 = StCall @{ pageSize='50'; pageNumber='1' }
$p2 = StCall @{ pageSize='50'; pageNumber='2' }
$inter = @(Compare-Object ($p1.data | ForEach-Object { $_.itemCode }) ($p2.data | ForEach-Object { $_.itemCode }) -IncludeEqual -ExcludeDifferent)
Check 'paging: page1 & page2 disjoint, same totalRows' ($inter.Count -eq 0 -and $p1.totalRows -eq $p2.totalRows -and $p1.data.Count -eq 50) "overlap=$($inter.Count) p1rows=$($p1.data.Count)"
$pbig = StCall @{ pageSize='50'; pageNumber='99999' }
Check 'page beyond end: success + 0 rows' ($pbig.success -eq $true -and $pbig.data.Count -eq 0) "rows=$($pbig.data.Count)"
# totals stable across pages
Check 'paging: totals identical on p1/p2 (whole-set totals)' (NearEq $p1.totals.totalSalesNet $p2.totals.totalSalesNet) ''

Write-Output ''
Write-Output '=== 9. SIZE SEQUENCE ORDERING ==='
$seqCount = (DbQuery 'SELECT COUNT(*) c FROM tbl_SizeSequence').Rows[0].c
if ($seqCount -gt 0) {
    $dsz = StCall @{ primaryGroup='Size'; summary='true'; sortBySizeSequence='true' }
    $seqMap = @{}
    foreach ($r in (DbQuery "SELECT ISNULL(sz.SizeInvoiceDescr,'N/A') d, MIN(ss.SizeSequence) s FROM tbl_SizeSequence ss INNER JOIN tbl_Size sz ON ss.fk_SizeID=sz.pk_SizeID GROUP BY ISNULL(sz.SizeInvoiceDescr,'N/A')").Rows) { $seqMap[$r.d] = [int]$r.s }
    $lastSeq = -1; $ordered = $true
    foreach ($r in $dsz.data) {
        $s = 99999; if ($seqMap.ContainsKey($r.level1Value)) { $s = $seqMap[$r.level1Value] }
        if ($s -lt $lastSeq) { $ordered = $false; break }
        $lastSeq = $s
    }
    Check 'Size + sortBySizeSequence: rows follow tbl_SizeSequence order' ($dsz.success -eq $true -and $ordered) "brokeAt seq=$lastSeq"
} else { Write-Output '  INFO  tbl_SizeSequence empty on demo DB - ordering check skipped' }

Write-Output ''
Write-Output '=== 10. EXPORTS — EXACT NUMBERS ==='
# CSV (no grouping) - parse and compare exactly to endpoint totals
function AsText($content) {
    if ($content -is [byte[]]) { return [System.Text.Encoding]::UTF8.GetString($content) }
    return [string]$content
}
$csvResp = Invoke-WebRequest ("$base/Reports/ExportSalesThroughCsv?" + (StQs @{})) -WebSession $session -UseBasicParsing -TimeoutSec 180
Check 'CSV: content-type text/csv' ($csvResp.Headers['Content-Type'] -match 'text/csv') $csvResp.Headers['Content-Type']
$csvLines = (AsText $csvResp.Content) -split "`r?`n"
$dataLines = @($csvLines | Where-Object { $_ -and -not $_.StartsWith('#') })
$headerLine = $dataLines[0]
$totalLine = @($dataLines | Where-Object { $_ -match '^TOTAL,|,TOTAL,|^"TOTAL"' })[-1]
$bodyLines = @($dataLines | Select-Object -Skip 1 | Where-Object { $_ -notmatch 'Subtotal:' -and $_ -ne $totalLine })
Check 'CSV: data row count == totalRows' ($bodyLines.Count -eq $d.totalRows) "csv=$($bodyLines.Count) api=$($d.totalRows)"
$totCells = $totalLine -split ','
# layout (no grouping): ItemCode,ItemName,IntakeQty,IntakeValue,SalesQty,SalesNet,SalesGross,...
Check 'CSV: TOTAL IntakeValue == endpoint totals' (NearEq $totCells[3] $d.totals.totalIntakeValue) "csv=$($totCells[3]) api=$($d.totals.totalIntakeValue)"
Check 'CSV: TOTAL SalesNet == endpoint totals'    (NearEq $totCells[5] $d.totals.totalSalesNet) "csv=$($totCells[5]) api=$($d.totals.totalSalesNet)"
# CSV row-level: first data row equals endpoint first row (same default sort)
$firstCsv = $bodyLines[0] -split ','
$firstApi = $d.data[0]
Check 'CSV: first row ItemCode+SalesNet == endpoint row' ($firstCsv[0].Trim('"') -eq $firstApi.itemCode -and (NearEq $firstCsv[5] $firstApi.salesNet)) "csv=$($firstCsv[0]),$($firstCsv[5]) api=$($firstApi.itemCode),$($firstApi.salesNet)"
# CSV grouped: subtotal lines exist and grand total unchanged
$csvG = Invoke-WebRequest ("$base/Reports/ExportSalesThroughCsv?" + (StQs @{ primaryGroup='Category'; secondaryGroup='Brand'; thirdGroup='Season' })) -WebSession $session -UseBasicParsing -TimeoutSec 180
$gLines = (AsText $csvG.Content) -split "`r?`n"
$subCount = @($gLines | Where-Object { $_ -match 'Subtotal:' }).Count
$gTotalLine = @($gLines | Where-Object { $_ -match ',TOTAL,' })[-1]
$gt = $gTotalLine -split ','
Check 'CSV grouped: has subtotal rows' ($subCount -gt 0) "subtotals=$subCount"
Check 'CSV grouped: TOTAL SalesNet == endpoint totals' (NearEq $gt[8] $d.totals.totalSalesNet) "csv=$($gt[8]) api=$($d.totals.totalSalesNet)"
# Excel + PDF magic bytes
$xls = Invoke-WebRequest ("$base/Reports/ExportSalesThroughExcel?" + (StQs @{})) -WebSession $session -UseBasicParsing -TimeoutSec 180
Check 'Excel: xlsx content + PK magic' ($xls.Headers['Content-Type'] -match 'spreadsheetml' -and $xls.Content[0] -eq 0x50 -and $xls.Content[1] -eq 0x4B) $xls.Headers['Content-Type']
$pdf = Invoke-WebRequest ("$base/Reports/ExportSalesThroughPdf?" + (StQs @{})) -WebSession $session -UseBasicParsing -TimeoutSec 180
$pdfMagic = [System.Text.Encoding]::ASCII.GetString($pdf.Content[0..3])
Check 'PDF: application/pdf + %PDF magic' ($pdf.Headers['Content-Type'] -match 'pdf' -and $pdfMagic -eq '%PDF') $pdfMagic
# Print preview: tfoot totals equal endpoint totals
$pv = Invoke-WebRequest ("$base/Reports/SalesThroughPrintPreview?" + (StQs @{ primaryGroup='Category'; secondaryGroup='Brand'; thirdGroup='Season' })) -WebSession $session -UseBasicParsing -TimeoutSec 180
$tfoot = [regex]::Match($pv.Content, '<tfoot>.*?</tfoot>', 'Singleline').Value
$nums = [regex]::Matches($tfoot, '<td class="num">([\d,.\-]+)%?</td>') | ForEach-Object { $_.Groups[1].Value.Replace(',','') }
Check 'Preview: grand total IntakeValue == endpoint' (NearEq $nums[1] $d.totals.totalIntakeValue) "pv=$($nums[1]) api=$($d.totals.totalIntakeValue)"
Check 'Preview: grand total SalesNet == endpoint' (NearEq $nums[3] $d.totals.totalSalesNet) "pv=$($nums[3]) api=$($d.totals.totalSalesNet)"
# Export with filter must differ from unfiltered (filter propagation to exports)
if ($cats.Rows.Count -ge 2) {
    $selJson = '{"categories":{"ids":["' + $cats.Rows[0].id + '"],"mode":"include"}}'
    $csvF = Invoke-WebRequest ("$base/Reports/ExportSalesThroughCsv?" + (StQs @{ itemsSelectionJson=$selJson })) -WebSession $session -UseBasicParsing -TimeoutSec 180
    $fLines = (AsText $csvF.Content) -split "`r?`n"
    $fTotal = @($fLines | Where-Object { $_ -match '^TOTAL,|,TOTAL,|^"TOTAL"' })[-1] -split ','
    $dfilt = StCall @{ itemsSelection=$selJson }
    Check 'CSV with category filter: TOTAL == filtered endpoint totals' (NearEq $fTotal[5] $dfilt.totals.totalSalesNet) "csv=$($fTotal[5]) api=$($dfilt.totals.totalSalesNet)"
}

Write-Output ''
Write-Output '=== 11. SUMMARY-MODE CSV/PREVIEW CELL ALIGNMENT ==='
$csvS = Invoke-WebRequest ("$base/Reports/ExportSalesThroughCsv?" + (StQs @{ primaryGroup='Category'; secondaryGroup='Brand'; thirdGroup='Season'; summary='true' })) -WebSession $session -UseBasicParsing -TimeoutSec 180
$sLines = @(((AsText $csvS.Content) -split "`r?`n") | Where-Object { $_ -and -not $_.StartsWith('#') })
$hdrCells = ($sLines[0] -split ',').Count
$badRows = 0
foreach ($ln in ($sLines | Select-Object -Skip 1)) { $c = ($ln -split ',').Count; if ($c -ne $hdrCells) { $badRows++ } }
Check 'Summary CSV: every row has header cell count (incl. subtotals/total)' ($badRows -eq 0) "misaligned=$badRows expected=$hdrCells"

Write-Output ''
Write-Output '=== 12. SCHEDULE CRUD ==='
$schedParams = '{"reportType":"SalesThrough","dateFrom":"' + $DF + '","dateTo":"' + $DT + '","stSummary":true,"stPrimaryGroup":"Category","stSecondaryGroup":"Brand","stThirdGroup":"None","stIncludeAdditionalCharges":true,"stSortBySizeSequence":false,"storeCodes":"","sortColumn":"ItemCode","sortDirection":"ASC","reportDateRange":{"type":"LastNDays","value":30}}'
$sv = (Invoke-WebRequest "$base/Reports/SaveStSchedule" -Method Post -Body @{
    scheduleName='QA ST Full E2E'; recurrenceType='Weekly'; scheduleTime='07:30'; exportFormat='Excel';
    recipients='qa-noreply@powersoft.com.cy'; emailSubject='QA ST'; parametersJson=$schedParams;
    recurrenceJson='{"type":"Weekly","time":"07:30","range":{"startDate":"2026-07-14","noEndDate":true},"pattern":{"interval":1,"daysOfWeek":[2]}}'
} -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Check 'Schedule save: success + id' ($sv.success -eq $true -and $sv.scheduleId -gt 0) ($sv | ConvertTo-Json -Compress)
if ($sv.scheduleId) {
    $lst = (Invoke-WebRequest "$base/Reports/GetStSchedules" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    $mine = @($lst.schedules | Where-Object { $_.scheduleId -eq $sv.scheduleId })
    Check 'Schedule list: contains saved schedule' ($mine.Count -eq 1) "found=$($mine.Count)"
    $back = (Invoke-WebRequest ("$base/Reports/GetScheduleById?scheduleId=" + $sv.scheduleId) -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    $pj = $back.schedule.parametersJson | ConvertFrom-Json
    Check 'Schedule readback: ST params round-trip (groups intact)' ($pj.stPrimaryGroup -eq 'Category' -and $pj.stSecondaryGroup -eq 'Brand' -and $pj.stSummary -eq $true) ($back.schedule.parametersJson)
    Check 'Schedule readback: nextRun computed' (-not [string]::IsNullOrEmpty($back.schedule.nextRun)) ''
    $del = (Invoke-WebRequest "$base/Reports/DeleteSchedule" -Method Post -Body @{ scheduleId=$sv.scheduleId } -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    Check 'Schedule delete: success' ($del.success -eq $true) ($del | ConvertTo-Json -Compress)
}

Write-Output ''
Write-Output '=== 13. SAVE LAYOUT ROUNDTRIP ==='
$layoutBody = '{"Summary":"1","PrimaryGroup":"Category","SecondaryGroup":"Brand","ThirdGroup":"Season","IncludeAdditionalCharges":"0","SortBySizeSequence":"1","PageSize":"250"}'
$slr = (Invoke-WebRequest "$base/Reports/SaveReportLayout?reportType=SalesThrough" -Method Post -Body $layoutBody -ContentType 'application/json' -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Check 'Layout save: success' ($slr.success -eq $true) ($slr | ConvertTo-Json -Compress)
$glr = (Invoke-WebRequest "$base/Reports/GetReportLayout?reportType=SalesThrough" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Check 'Layout get: returns saved values' ($glr.success -eq $true -and $glr.hasSaved -eq $true -and $glr.parameters.PrimaryGroup -eq 'Category' -and $glr.parameters.PageSize -eq '250') ($glr | ConvertTo-Json -Compress)
$rlr = (Invoke-WebRequest "$base/Reports/ResetReportLayout?reportType=SalesThrough" -Method Post -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
$glr2 = (Invoke-WebRequest "$base/Reports/GetReportLayout?reportType=SalesThrough" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Check 'Layout reset: cleared' ($rlr.success -eq $true -and $glr2.hasSaved -ne $true) ($glr2 | ConvertTo-Json -Compress)

Write-Output ''
Write-Output '=== 14. SEND EMAIL (validation only - no real send) + AI ANALYZE ==='
try {
    $em = (Invoke-WebRequest "$base/Reports/SendSalesThroughReportEmail" -Method Post -Body @{
        recipients=''; exportFormat='Excel'; dateFrom=$DF; dateTo=$DT
    } -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    Check 'SendEmail: empty recipients rejected' ($em.success -eq $false) ($em | ConvertTo-Json -Compress)
} catch { Check 'SendEmail: empty recipients rejected (4xx)' ($_.Exception.Response.StatusCode.value__ -ge 400) $_.Exception.Message }
# AI analyze - small dataset (summary by category) to limit token cost
$aiRaw = (Invoke-WebRequest "$base/Reports/AnalyzeSalesThroughReport" -Method Post -Body @{
    dateFrom=$DF; dateTo=$DT; summary='true'; primaryGroup='Category'; secondaryGroup='None'; thirdGroup='None';
    includeAdditionalCharges='true'; sortBySizeSequence='false'; storeCodes=''; itemsSelectionJson='';
    sortColumn='ItemCode'; sortDirection='ASC'; locale='en'
} -WebSession $session -UseBasicParsing -TimeoutSec 300).Content
$ai = $aiRaw | ConvertFrom-Json
# analysis is a structured object: { summary, keyFindings, ... }
Check 'AI analyze: success + structured analysis with summary' ($ai.success -eq $true -and $ai.analysis.summary.Length -gt 30) ("len=" + $aiRaw.Length + " msg=" + $ai.message)

Write-Output ''
Write-Output '=== 15. PAGE WIRING + AUTH ==='
$page = Invoke-WebRequest "$base/Reports/SalesThrough" -WebSession $session -UseBasicParsing -TimeoutSec 60
$c = $page.Content
$wired = ($c -match 'function collectScheduleParameters') -and ($c -match 'function collectAiFormData') -and
         ($c -match 'collectEmailParameters') -and ($c -match 'function collectCurrentLayout') -and
         ($c -match 'id="schedDateRangeType"') -and ($c -match 'getItemsSelectionFilter') -and
         ($c -match 'stGroupRows') -and ($c -match 'stSubtotalRow')
Check 'Page: schedule/email/AI/layout/items-selection/nested-grouping wired' $wired ''
# unauthenticated export must redirect to login, not leak data (raw WebRequest — PS5-safe 302 capture)
$anonReq = [System.Net.WebRequest]::Create("$base/Reports/ExportSalesThroughCsv?" + (StQs @{}))
$anonReq.AllowAutoRedirect = $false
$anonStatus = 0; $anonLoc = ''
try { $anonResp = $anonReq.GetResponse(); $anonStatus = [int]$anonResp.StatusCode; $anonLoc = $anonResp.Headers['Location']; $anonResp.Close() }
catch { $r = $_.Exception.Response; if ($r) { $anonStatus = [int]$r.StatusCode; $anonLoc = $r.Headers['Location'] } }
Check 'Anon export: 302 redirect to login (no data leak)' ($anonStatus -eq 302 -and $anonLoc -match 'Account/Login') "status=$anonStatus loc=$anonLoc"

Write-Output ''
Write-Output '============================================================'
Write-Output ("RESULT: {0} passed, {1} failed" -f $script:pass, $script:fail)
if ($script:failures.Count -gt 0) { Write-Output 'FAILED CHECKS:'; $script:failures | ForEach-Object { Write-Output ("  - " + $_) } }

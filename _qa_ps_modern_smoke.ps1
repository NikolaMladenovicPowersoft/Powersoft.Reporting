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

# 1) GET page — new visual shell present, all functional hooks intact
$get = Invoke-WebRequest "$base/Reports/PurchasesSales" -WebSession $session -UseBasicParsing -TimeoutSec 60
Check 'GET 200' ($get.StatusCode -eq 200)
Check 'modern wrapper present' ($get.Content -match 'class="rpt-modern"')
Check 'hero present' ($get.Content -match 'rpt-hero')
Check 'modern css linked' ($get.Content -match 'report-modern\.css')
Check 'kpi/empty state present' ($get.Content -match 'rpt-empty')
# functional hooks (IDs that all JS depends on)
foreach ($id in @('reportForm','generateBtn','DateFrom','DateTo','ReportMode','PrimaryGroup','SecondaryGroup','ThirdGroup',
    'includeVatCheck','showProfitCheck','showStockCheck','showOnOrderCheck','showReservationCheck','showAvailableCheck',
    'includeAdditionalChargesCheck','sortBySizeSequenceCheck','sortBySizeSequenceWrap','selectedStoreCodesHidden',
    'itemsSelectionJsonHidden','pageNumberHidden','sortColumnHidden','sortDirectionHidden','hiddenColumnsInput',
    'presetToday','presetLastYear','scheduleModal','columnSettingsModal','docDetailModal')) {
    Check "hook #$id" ($get.Content -match ('id="' + $id + '"')) 'MISSING'
}
# date preset radios kept identical values
foreach ($v in @('today','yesterday','last7','last30','thisMonth','lastMonth','ytd','lastYear')) {
    Check "preset value $v" ($get.Content -match ('name="DatePreset"[^>]*value="' + $v + '"'))
}

# 2) POST generate with grouping — results, KPIs, nested groups, grand total all render
$body = @{
    DateFrom='2025-01-01'; DateTo='2025-03-31'; ReportMode='0'
    PrimaryGroup='1'; SecondaryGroup='2'; ThirdGroup='0'
    IncludeVat='false'; ShowProfit='true'; ShowStock='true'
    PageSize='50'; PageNumber='1'
}
$post = Invoke-WebRequest "$base/Reports/PurchasesSales" -Method Post -Body $body -WebSession $session -UseBasicParsing -TimeoutSec 180
Check 'POST 200' ($post.StatusCode -eq 200)
Check 'results table rendered' ($post.Content -match 'id="resultsTable"')
Check 'kpi cards rendered' (([regex]::Matches($post.Content, 'rpt-kpi ')).Count -ge 4)
Check 'countup markers present' ($post.Content -match 'data-rm-countup')
Check 'group header rows rendered' ($post.Content -match 'group-header-row')
Check 'subtotal rows rendered' ($post.Content -match 'group-subtotal-row')
Check 'grand total rendered' ($post.Content -match 'grand-total-row')
Check 'sticky table wrap' ($post.Content -match 'rpt-table-wrap')
Check 'drill-down links intact' ($post.Content -match 'dd-link-purch' -and $post.Content -match 'dd-link-sales')
Check 'sortable headers intact' ($post.Content -match "onclick=`"sortBy\('QuantityPurchased'\)`"")

# numbers unchanged by the restyle: compare totals row against a pre-restyle reference run is not
# possible here, but totals must still equal the sum semantics — grab KPI + grand total and check
# they are both present and identical values (KPI TotalPurchasedQty == grand total first numeric cell)
$kpiQty = [regex]::Match($post.Content, 'data-rm-countup>([\d,]+)</div>\s*<div class="stat-label">Total Qty Purchased').Groups[1].Value
Check 'KPI qty parsed' ($kpiQty -ne '') $kpiQty
if ($kpiQty) {
    $gt = [regex]::Match($post.Content, '(?s)grand-total-row.*?</tr>').Value
    Check 'grand total contains same qty' ($gt -match [regex]::Escape($kpiQty))
}

# 3) monthly mode still renders with two-row sticky header
$body2 = @{ DateFrom='2025-01-01'; DateTo='2025-12-31'; ReportMode='2'; PrimaryGroup='1'; SecondaryGroup='0'; ThirdGroup='0'; PageSize='50'; PageNumber='1' }
$post2 = Invoke-WebRequest "$base/Reports/PurchasesSales" -Method Post -Body $body2 -WebSession $session -UseBasicParsing -TimeoutSec 180
Check 'monthly POST 200' ($post2.StatusCode -eq 200)
Check 'monthly table rendered' ($post2.Content -match 'col-purch' -and $post2.Content -match 'col-sales')

# 4) empty result state (far future dates)
$body3 = @{ DateFrom='2031-01-01'; DateTo='2031-01-02'; ReportMode='0'; PrimaryGroup='0'; SecondaryGroup='0'; ThirdGroup='0'; PageSize='50'; PageNumber='1' }
$post3 = Invoke-WebRequest "$base/Reports/PurchasesSales" -Method Post -Body $body3 -WebSession $session -UseBasicParsing -TimeoutSec 120
Check 'empty state rendered' ($post3.Content -match 'No Data Found')

# 5) exports still work after restyle (form-posted params)
$exp = @{
    dateFrom='2025-01-01'; dateTo='2025-01-31'; reportMode='0'; primaryGroup='1'; secondaryGroup='0'; thirdGroup='0'
    includeVat='false'; showProfit='true'; showStock='false'; showOnOrder='false'; showReservation='false'; showAvailable='false'
    includeAdditionalCharges='false'; sortBySizeSequence='false'; storeCodes=''; itemIds=''; sortColumn='ItemCode'; sortDirection='ASC'; ItemsSelectionJson=''
}
$csv = Invoke-WebRequest "$base/Reports/ExportPsCsv" -Method Post -Body $exp -WebSession $session -UseBasicParsing -TimeoutSec 120
Check 'CSV export 200' ($csv.StatusCode -eq 200)
$xls = Invoke-WebRequest "$base/Reports/ExportPsExcel" -Method Post -Body $exp -WebSession $session -UseBasicParsing -TimeoutSec 120
Check 'Excel export 200 + magic' ($xls.StatusCode -eq 200 -and $xls.Content[0] -eq 0x50 -and $xls.Content[1] -eq 0x4B)
$pdf = Invoke-WebRequest "$base/Reports/ExportPsPdf" -Method Post -Body $exp -WebSession $session -UseBasicParsing -TimeoutSec 120
Check 'PDF export 200 + magic' ($pdf.StatusCode -eq 200 -and [System.Text.Encoding]::ASCII.GetString($pdf.Content[0..3]) -eq '%PDF')
$prev = Invoke-WebRequest "$base/Reports/PrintPsPreview" -Method Post -Body $exp -WebSession $session -UseBasicParsing -TimeoutSec 120
Check 'print preview 200' ($prev.StatusCode -eq 200 -and $prev.Content -match '<table')

Write-Output ''
Write-Output ("RESULT: {0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }

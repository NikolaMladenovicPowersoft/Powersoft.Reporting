$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5150'
$df = '2023-01-01'; $dt = '2026-12-31'
$results = New-Object System.Collections.ArrayList
$session = $null

function Add-Result($name, $ok, $detail) {
  [void]$results.Add([pscustomobject]@{ Test=$name; Status=$(if($ok){'PASS'}else{'FAIL'}); Detail=$detail })
}

function Test-Page($name, $url, [int]$min=300) {
  try {
    $r = Invoke-WebRequest "$base$url" -WebSession $session -UseBasicParsing -TimeoutSec 180
    $ok = ($r.StatusCode -eq 200) -and ($r.RawContentLength -gt $min)
    Add-Result $name $ok "HTTP $($r.StatusCode), len=$($r.RawContentLength)"
  } catch { Add-Result $name $false ("EXC: " + $_.Exception.Message) }
}

function Test-Json($name, $url, $method='Get') {
  try {
    $r = Invoke-WebRequest "$base$url" -Method $method -WebSession $session -UseBasicParsing -TimeoutSec 240
    $okHttp = $r.StatusCode -eq 200
    $detail = "HTTP $($r.StatusCode)"
    $okJson = $true
    try {
      $obj = $r.Content | ConvertFrom-Json
      if ($obj.PSObject.Properties.Name -contains 'success') {
        $okJson = [bool]$obj.success
        if (-not $okJson) { $detail += " success=false msg=$($obj.message)" }
      } elseif ($obj.PSObject.Properties.Name -contains 'error') {
        $okJson = $false; $detail += " error=$($obj.error)"
      }
    } catch { $okJson = $false; $detail += " (non-JSON resp)" }
    Add-Result $name ($okHttp -and $okJson) $detail
  } catch { Add-Result $name $false ("EXC: " + $_.Exception.Message) }
}

function Test-File($name, $url, $expectType) {
  try {
    $r = Invoke-WebRequest "$base$url" -WebSession $session -UseBasicParsing -TimeoutSec 240
    $ct = [string]$r.Headers['Content-Type']
    $ok = ($r.StatusCode -eq 200) -and ($r.RawContentLength -gt 200) -and ($ct -like "*$expectType*")
    Add-Result $name $ok "HTTP $($r.StatusCode), type=$ct, bytes=$($r.RawContentLength)"
  } catch { Add-Result $name $false ("EXC: " + $_.Exception.Message) }
}

# ===== 1. LOGIN =====
$login = Invoke-WebRequest "$base/Account/Login" -SessionVariable session -UseBasicParsing -TimeoutSec 30
$token = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
if (-not $token) { $token = [regex]::Match($login.Content, 'value="([^"]+)"[^>]*name="__RequestVerificationToken"').Groups[1].Value }
$body = @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token }
try {
  $r = Invoke-WebRequest "$base/Account/Login" -Method Post -Body $body -WebSession $session -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 30
  $code = $r.StatusCode
} catch { $code = $_.Exception.Response.StatusCode.value__ }
Add-Result 'AUTH: Login (REPORTING_TEST)' ($code -eq 302) "HTTP $code (302=success)"

# ===== 2. CONNECT DB =====
try {
  $r = Invoke-WebRequest "$base/Home/Connect" -Method Post -Body @{ databaseCode='DEMO365MODAPRO1' } -WebSession $session -UseBasicParsing -TimeoutSec 60
  $j = $r.Content | ConvertFrom-Json
  Add-Result 'HOME: Connect DEMO365MODAPRO1' ([bool]$j.success) ("db=" + $j.databaseName)
} catch { Add-Result 'HOME: Connect DEMO365MODAPRO1' $false ("EXC: " + $_.Exception.Message) }

Test-Page 'HOME: DB selection page' '/Home/Index'
Test-Json 'HOME: GetDatabases(DEMOS)' '/Home/GetDatabases?companyCode=DEMOS'

# ===== 3. REPORT PAGES (screen loads) =====
Test-Page 'PAGE: Dashboard'         '/Reports'
Test-Page 'PAGE: AverageBasket'     '/Reports/AverageBasket'
Test-Page 'PAGE: PurchasesSales'    '/Reports/PurchasesSales'
Test-Page 'PAGE: Pareto'            '/Reports/Pareto'
Test-Page 'PAGE: Charts'            '/Reports/Charts'
Test-Page 'PAGE: Catalogue'         '/Reports/Catalogue'
Test-Page 'PAGE: BelowMinStock'     '/Reports/BelowMinStock'
Test-Page 'PAGE: CancelLog'         '/Reports/CancelLog'
Test-Page 'PAGE: ProspectClients'   '/Reports/ProspectClients'
Test-Page 'PAGE: OffersReport'      '/Reports/OffersReport'
Test-Page 'PAGE: ScheduleLogs'      '/Reports/ScheduleLogs'

# ===== 4. SHARED LOOKUPS / FILTER DATA (buttons that fetch) =====
Test-Json 'LOOKUP: GetStores'             '/Reports/GetStores'
Test-Json 'LOOKUP: SearchItems(a)'        '/Reports/SearchItems?search=a'
Test-Json 'LOOKUP: GetDimensions Category' '/Reports/GetDimensions?type=Category'
Test-Json 'LOOKUP: GetDimensions Brand'    '/Reports/GetDimensions?type=Brand'
Test-Json 'LOOKUP: GetDimensions Department' '/Reports/GetDimensions?type=Department'
Test-Json 'LOOKUP: GetDimensions Supplier' '/Reports/GetDimensions?type=Supplier'
Test-Json 'LOOKUP: GetDimensions Customer'  '/Reports/GetDimensions?type=Customer'
Test-Json 'LOOKUP: GetDimensions Season'   '/Reports/GetDimensions?type=Season'
Test-Json 'LOOKUP: GetAiStatus'           '/Reports/GetAiStatus'
Test-Json 'LOOKUP: GetEmailTemplates'     '/Reports/GetEmailTemplates?reportType=PurchasesSales'
Test-Json 'LOOKUP: GetAiPromptTemplates'  '/Reports/GetAiPromptTemplates?reportType=PurchasesSales'
Test-Json 'LOOKUP: GetFilterPresets'      '/Reports/GetFilterPresets?reportType=PurchasesSales'
Test-Json 'LOOKUP: ProspectClients lookups' '/Reports/GetProspectClientsLookups'
Test-Json 'LOOKUP: OffersReport lookups'  '/Reports/GetOffersReportLookups'

# ===== 5. LAYOUTS + SCHEDULES (GET) =====
Test-Json 'LAYOUT: GetLayout (AB)'        '/Reports/GetLayout'
Test-Json 'LAYOUT: GetParetoLayout'       '/Reports/GetParetoLayout'
Test-Json 'LAYOUT: ListAbLayouts'         '/Reports/ListAbLayouts'
Test-Json 'LAYOUT: ListChartLayouts'      '/Reports/ListChartLayouts'
Test-Json 'LAYOUT: ListCatalogueLayouts'  '/Reports/ListCatalogueLayouts'
Test-Json 'LAYOUT: ListCancelLogLayouts'  '/Reports/ListCancelLogLayouts'
Test-Json 'LAYOUT: ListProspectLayouts'   '/Reports/ListProspectClientsLayouts'
Test-Json 'LAYOUT: ListOffersLayouts'     '/Reports/ListOffersReportLayouts'
Test-Json 'SCHED: GetSchedules (AB)'      '/Reports/GetSchedules'
Test-Json 'SCHED: GetPsSchedules'         '/Reports/GetPsSchedules'
Test-Json 'SCHED: GetParetoSchedules'     '/Reports/GetParetoSchedules'
Test-Json 'SCHED: GetChartSchedules'      '/Reports/GetChartSchedules'
Test-Json 'SCHED: GetCatalogueSchedules'  '/Reports/GetCatalogueSchedules'
Test-Json 'SCHED: GetCancelLogSchedules'  '/Reports/GetCancelLogSchedules'
Test-Json 'SCHED: GetBmsSchedules'        '/Reports/GetBmsSchedules'
Test-Json 'SCHED: GetProspectSchedules'   '/Reports/GetProspectClientsSchedules'
Test-Json 'SCHED: GetOffersSchedules'     '/Reports/GetOffersReportSchedules'
Test-Json 'SCHED: GetScheduleLogs'        '/Reports/GetScheduleLogs'

# ===== 6. PURCHASES vs SALES (P0) — GENERATE + EXPORTS =====
$psAll = "dateFrom=$df&dateTo=$dt&reportMode=Summary&primaryGroup=None&secondaryGroup=None&thirdGroup=None&includeVat=false&showProfit=true&showStock=true&showOnOrder=true&showReservation=true&showAvailable=true&includeAdditionalCharges=true&sortColumn=ItemCode&sortDirection=ASC"
$psWholesale = "dateFrom=$df&dateTo=$dt&reportMode=Summary&primaryGroup=Brand&secondaryGroup=None&thirdGroup=None&includeVat=false&showProfit=true&showStock=true&showOnOrder=true&showReservation=true&showAvailable=true&includeAdditionalCharges=false&sortColumn=ItemCode&sortDirection=ASC"
$psGrouped = "dateFrom=$df&dateTo=$dt&reportMode=Summary&primaryGroup=Category&secondaryGroup=Brand&thirdGroup=None&includeVat=true&showProfit=true&showStock=true&showOnOrder=true&showReservation=true&showAvailable=true&includeAdditionalCharges=true&sortColumn=ItemCode&sortDirection=ASC"
Test-File 'PS: Excel (all qty, incl charges)'   "/Reports/ExportPsExcel?$psAll" 'spreadsheetml'
Test-File 'PS: Excel (wholesale only)'          "/Reports/ExportPsExcel?$psWholesale" 'spreadsheetml'
Test-File 'PS: CSV (all qty cols)'              "/Reports/ExportPsCsv?$psAll" 'csv'
Test-File 'PS: PDF (grouped Cat>Brand)'         "/Reports/ExportPsPdf?$psGrouped" 'pdf'
Test-Page 'PS: Print Preview (Available)'       "/Reports/PrintPsPreview?$psAll" 500

# ===== 7. CHARTS (P0 Show Others) =====
Test-Json 'CHART: data Sales pie'          "/Reports/GetChartData?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value&topN=10&showOthers=true&chartType=pie&mode=Sales" 'Post'
Test-Json 'CHART: data SalesVsPurchases'   "/Reports/GetChartData?dateFrom=$df&dateTo=$dt&dimension=Brand&metric=Value&topN=5&showOthers=true&chartType=bar&mode=SalesVsPurchases" 'Post'
Test-File 'CHART: Excel'                   "/Reports/ExportChartExcel?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value&topN=10&showOthers=true&chartType=pie&mode=Sales" 'spreadsheetml'
Test-File 'CHART: CSV'                      "/Reports/ExportChartCsv?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value&topN=10&showOthers=true&chartType=pie&mode=Sales" 'csv'
Test-File 'CHART: PDF'                      "/Reports/ExportChartPdf?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value&topN=10&showOthers=true&chartType=pie&mode=Sales" 'pdf'
Test-Page 'CHART: Print Preview'           "/Reports/PrintChartPreview?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value&topN=10&showOthers=true&chartType=pie&mode=Sales" 500

# ===== 8. PARETO (Category to avoid item timeout) =====
Test-Json 'PARETO: data (Category)'   "/Reports/GetParetoData?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value&showOthers=true" 'Post'
Test-File 'PARETO: Excel'             "/Reports/ExportParetoExcel?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value" 'spreadsheetml'
Test-File 'PARETO: CSV'               "/Reports/ExportParetoCsv?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value" 'csv'
Test-File 'PARETO: PDF'               "/Reports/ExportParetoPdf?dateFrom=$df&dateTo=$dt&dimension=Category&metric=Value" 'pdf'

# ===== 9. CATALOGUE =====
$catDf = '2024-01-01'  # Catalogue enforces a max 3-year date range
$cat = "dateFrom=$catDf&dateTo=$dt&reportMode=Summary&reportOn=Both&primaryGroup=Category&secondaryGroup=None&thirdGroup=None&showProfit=true&showStock=true&sortColumn=ItemCode&sortDirection=ASC"
Test-File 'CATALOGUE: Excel'         "/Reports/ExportCatalogueExcel?$cat" 'spreadsheetml'
Test-File 'CATALOGUE: CSV'           "/Reports/ExportCatalogueCsv?$cat" 'csv'
Test-Page 'CATALOGUE: Print Preview' "/Reports/PrintCataloguePreview?$cat" 500

# ===== 10. CANCEL LOG =====
Test-Json 'CANCELLOG: data'   "/Reports/GetCancelLogData?dateFrom=$df&dateTo=$dt&reportType=Detailed&actionType=All" 'Post'
Test-File 'CANCELLOG: CSV'    "/Reports/ExportCancelLogCsv?dateFrom=$df&dateTo=$dt&reportType=Detailed&actionType=All" 'csv'
Test-File 'CANCELLOG: Excel'  "/Reports/ExportCancelLogExcel?dateFrom=$df&dateTo=$dt&reportType=Detailed&actionType=All" 'spreadsheetml'

# ===== 11. BELOW MIN STOCK =====
Test-Json 'BMS: data' "/Reports/GetBelowMinStockData" 'Post'

# ===== 12. OFFERS =====
Test-Json 'OFFERS: data'  "/Reports/GetOffersReportData?dateFrom=$df&dateTo=$dt" 'Post'
Test-File 'OFFERS: CSV'   "/Reports/ExportOffersReportCsv?dateFrom=$df&dateTo=$dt" 'csv'
Test-File 'OFFERS: Excel' "/Reports/ExportOffersReportExcel?dateFrom=$df&dateTo=$dt" 'spreadsheetml'

# ===== 13. PROSPECT CLIENTS (tbl_ProspectClient may not exist in this tenant) =====
Test-Json 'PROSPECT: data'  "/Reports/GetProspectClientsData?dateFrom=$df&dateTo=$dt" 'Post'

# ===== 14. AVERAGE BASKET exports =====
$ab = "dateFrom=$df&dateTo=$dt&breakdown=Monthly&groupBy=None&secondaryGroupBy=None&includeVat=false&compareLastYear=false&sortColumn=Period&sortDirection=ASC"
Test-File 'AB: Excel'         "/Reports/ExportExcel?$ab" 'spreadsheetml'
Test-File 'AB: CSV'           "/Reports/ExportCsv?$ab" 'csv'
Test-File 'AB: PDF'           "/Reports/ExportPdf?$ab" 'pdf'
Test-Page 'AB: Print Preview' "/Reports/PrintPreview?$ab" 500

# ===== 15. NUMERIC CHECK: PS Available = Stock - Reserved + OnOrder (totals) =====
try {
  $r = Invoke-WebRequest "$base/Reports/ExportPsCsv?$psAll" -WebSession $session -UseBasicParsing -TimeoutSec 240
  $lines = ($r.Content -split "`r?`n") | Where-Object { $_ -and ($_ -notmatch '^\s*#') }
  $hdr = ($lines | Where-Object { $_ -match 'Available' } | Select-Object -First 1)
  $cols = $hdr.Split(',')
  function ColIdx($name){ for($i=0;$i -lt $cols.Count;$i++){ if($cols[$i].Trim('"').Trim() -eq $name){ return $i } }; return -1 }
  $iStock = ColIdx 'Stock'; if($iStock -lt 0){ $iStock = ColIdx 'Stock Qty' }
  $iOn = ColIdx 'On Order'; $iRes = ColIdx 'Reserved'; $iAv = ColIdx 'Available'
  $totRow = ($lines | Where-Object { ($_.Split(',')[0].Trim('"')) -eq 'TOTAL' } | Select-Object -First 1)
  if (-not $totRow) { $totRow = ($lines | Where-Object { $_ -match 'TOTAL' } | Select-Object -Last 1) }
  $tc = $totRow.Split(',')
  $stock=[decimal]($tc[$iStock].Trim('"')); $on=[decimal]($tc[$iOn].Trim('"')); $res=[decimal]($tc[$iRes].Trim('"')); $av=[decimal]($tc[$iAv].Trim('"'))
  $expected = $stock - $res + $on
  $ok = [math]::Abs($expected - $av) -lt 0.5
  Add-Result 'NUMERIC: PS Available=Stock-Reserved+OnOrder' $ok "Stock=$stock OnOrder=$on Reserved=$res Available=$av expected=$expected"
  Add-Result 'NUMERIC: PS totals non-zero (F1 fix)' (($stock -ne 0) -or ($on -ne 0) -or ($res -ne 0)) "Stock=$stock OnOrder=$on Reserved=$res"
} catch { Add-Result 'NUMERIC: PS Available formula' $false ("EXC: " + $_.Exception.Message) }

# ===== SUMMARY =====
Write-Host ""
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$pass = ($results | Where-Object Status -eq 'PASS').Count
$fail = ($results | Where-Object Status -eq 'FAIL').Count
Write-Host "============================================"
Write-Host "TOTAL: $($results.Count)  PASS: $pass  FAIL: $fail"
Write-Host "============================================"
if ($fail -gt 0) { Write-Host "FAILURES:"; $results | Where-Object Status -eq 'FAIL' | ForEach-Object { Write-Host (" - " + $_.Test + " :: " + $_.Detail) } }

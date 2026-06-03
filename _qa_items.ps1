$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5150'
$df = '2023-01-01'; $dt = '2026-12-31'
$sqlServer = 'ANDREASPS\SQLDEVELOPER17PS'; $sqlUser='sa'; $sqlPass='SQLADMIN123!'; $tenantDb='pswaDEMO365MODAPRO1'
$results = New-Object System.Collections.ArrayList
$session = $null

function Add-Result($name, $ok, $detail) {
  [void]$results.Add([pscustomobject]@{ Test=$name; Status=$(if($ok){'PASS'}else{'FAIL'}); Detail=$detail })
}

# ---- login + connect ----
$login = Invoke-WebRequest "$base/Account/Login" -SessionVariable session -UseBasicParsing -TimeoutSec 30
$token = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
$body = @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token }
try { Invoke-WebRequest "$base/Account/Login" -Method Post -Body $body -WebSession $session -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 30 | Out-Null } catch {}
$conn = Invoke-WebRequest "$base/Home/Connect" -Method Post -Body @{ databaseCode='DEMO365MODAPRO1' } -WebSession $session -UseBasicParsing -TimeoutSec 60
$cj = $conn.Content | ConvertFrom-Json
Add-Result 'SETUP: login + connect' ([bool]$cj.success) ("db=" + $cj.databaseName)

# ---- 1. component asset / markup wiring ----
$page = Invoke-WebRequest "$base/Reports/PurchasesSales" -WebSession $session -UseBasicParsing -TimeoutSec 60
$html = $page.Content
Add-Result 'WIRING: page references external items-selection.js' ($html -match 'items-selection\.js') 'src tag present'
Add-Result 'WIRING: page calls ItemsSelectionInit'   ($html -match 'ItemsSelectionInit\(') 'init call present'
Add-Result 'WIRING: NO inline IIFE leftover'         (-not ($html -match 'const _selInclude')) 'inline state vars gone'
Add-Result 'WIRING: Show-selected summary panel'     ($html -match 'isSelectedSummary') 'summary container present'
Add-Result 'WIRING: Selected-only modal toggle'      ($html -match 'dimModalSelectedToggle') 'toggle present'
$js = Invoke-WebRequest "$base/js/items-selection.js" -WebSession $session -UseBasicParsing -TimeoutSec 30
Add-Result 'WIRING: js module 200 + clear-on-switch' (($js.StatusCode -eq 200) -and ($js.Content -match 'switchingActive')) "bytes=$($js.RawContentLength)"
Add-Result 'WIRING: js renderSelectedSummary present' ($js.Content -match 'renderSelectedSummary') 'fn present'

# ---- 2. pick a category that actually has activity ----
$cats = (Invoke-WebRequest "$base/Reports/GetDimensions?type=Category" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
# Prefer a high-volume category (DB top = 292) so the include sample is meaningful.
$cat = $cats | Where-Object { $_.id -eq '292' } | Select-Object -First 1
if (-not $cat) { $cat = $cats | Where-Object { $_.id -ne '__NA__' } | Select-Object -First 1 }
Add-Result 'DATA: GetDimensions Category' ([bool]$cat) ("cat id=$($cat.id) name=$($cat.name); total=$($cats.Count)")
$catId = $cat.id

# ---- helpers: PS CSV row counting (Summary, per-item rows) ----
$psBase = "dateFrom=$df&dateTo=$dt&reportMode=Summary&primaryGroup=None&secondaryGroup=None&thirdGroup=None&includeVat=false&showProfit=false&showStock=false&sortColumn=ItemCode&sortDirection=ASC"
function Get-PsCsv($selJson) {
  $url = "$base/Reports/ExportPsCsv?$psBase"
  if ($selJson) { $url += "&ItemsSelectionJson=" + [uri]::EscapeDataString($selJson) }
  $r = Invoke-WebRequest $url -WebSession $session -UseBasicParsing -TimeoutSec 240
  return $r.Content
}
function Parse-PsCsv($content) {
  $lines = ($content -split "`r?`n") | Where-Object { $_ -and ($_ -notmatch '^\s*#') }
  $hdrIdx = -1
  for ($i=0;$i -lt $lines.Count;$i++){ if ($lines[$i] -match 'Item\s*Code' -or $lines[$i] -match '"?ItemCode"?'){ $hdrIdx=$i; break } }
  if ($hdrIdx -lt 0) { return @{ count=0; codes=@() } }
  $data = @(); $codes=@()
  for ($i=$hdrIdx+1;$i -lt $lines.Count;$i++){
    $first = ($lines[$i].Split(',')[0]).Trim('"').Trim()
    if ($first -eq '' -or $first -match '^(TOTAL|GRAND|Grand)') { continue }
    $data += $lines[$i]; $codes += $first
  }
  return @{ count=$data.Count; codes=$codes }
}

$all = Parse-PsCsv (Get-PsCsv $null)
$incJson = '{"categories":{"ids":["' + $catId + '"],"mode":"include"}}'
$inc = Parse-PsCsv (Get-PsCsv $incJson)
$excJson = '{"categories":{"ids":["' + $catId + '"],"mode":"exclude"}}'
$exc = Parse-PsCsv (Get-PsCsv $excJson)

Add-Result 'APPLY: unfiltered rows > 0'        ($all.count -gt 0) "rows=$($all.count)"
Add-Result 'APPLY: include reduces rows'       (($inc.count -gt 0) -and ($inc.count -lt $all.count)) "inc=$($inc.count) all=$($all.count)"
Add-Result 'APPLY: exclude reduces rows'       (($exc.count -gt 0) -and ($exc.count -lt $all.count)) "exc=$($exc.count) all=$($all.count)"
# After the NULL fix, exclude keeps uncategorised rows, so include + exclude must equal the full set.
Add-Result 'APPLY: include+exclude == all (NULL-keep fix)' (($inc.count + $exc.count) -eq $all.count) "inc+exc=$($inc.count + $exc.count) all=$($all.count)"

# ---- 3. DB comparison: every included ItemCode must really have fk_CategoryID = catId ----
$sample = $inc.codes | Select-Object -First 40 | ForEach-Object { "'" + ($_ -replace "'","''") + "'" }
if ($sample.Count -gt 0) {
  $inList = ($sample -join ',')
  $q = "SET NOCOUNT ON; SELECT COUNT(*) FROM tbl_Item WHERE ItemCode IN ($inList) AND ISNULL(fk_CategoryID,-1) <> $catId;"
  $bad = (sqlcmd -S $sqlServer -U $sqlUser -P $sqlPass -d $tenantDb -h -1 -W -Q $q) | Select-Object -First 1
  $bad = ($bad -replace '[^\d-]','')
  Add-Result 'DB: every included item has the chosen category' ($bad -eq '0') "items_with_wrong_category=$bad (sampled $($sample.Count))"

  # cross-check: app-filtered count vs DB count of items in that category WITH activity in range
  $qCount = "SET NOCOUNT ON; SELECT COUNT(DISTINCT it.ItemCode) FROM tbl_Item it WHERE ISNULL(it.fk_CategoryID,-1)=$catId AND it.ItemCode IN ($inList);"
  $dbInCat = (sqlcmd -S $sqlServer -U $sqlUser -P $sqlPass -d $tenantDb -h -1 -W -Q $qCount) | Select-Object -First 1
  $dbInCat = ($dbInCat -replace '[^\d-]','')
  Add-Result 'DB: sampled codes all resolve in tbl_Item with that category' ([int]$dbInCat -eq $sample.Count) "db_in_cat=$dbInCat sampled=$($sample.Count)"
}

# excluded rows must NOT contain the chosen category (sample), and SHOULD contain NULL-category items
$exSample = $exc.codes | Select-Object -First 60 | ForEach-Object { "'" + ($_ -replace "'","''") + "'" }
if ($exSample.Count -gt 0) {
  $exIn = ($exSample -join ',')
  $qBadEx = "SET NOCOUNT ON; SELECT COUNT(*) FROM tbl_Item WHERE ItemCode IN ($exIn) AND ISNULL(fk_CategoryID,-1)=$catId;"
  $badEx = ((sqlcmd -S $sqlServer -U $sqlUser -P $sqlPass -d $tenantDb -h -1 -W -Q $qBadEx) | Select-Object -First 1) -replace '[^\d-]',''
  Add-Result 'DB: excluded rows never contain the chosen category' ($badEx -eq '0') "leaked=$badEx (sampled $($exSample.Count))"
  $qNullEx = "SET NOCOUNT ON; SELECT COUNT(*) FROM tbl_Item WHERE ItemCode IN ($exIn) AND fk_CategoryID IS NULL;"
  $nullEx = ((sqlcmd -S $sqlServer -U $sqlUser -P $sqlPass -d $tenantDb -h -1 -W -Q $qNullEx) | Select-Object -First 1) -replace '[^\d-]',''
  Add-Result 'DB: exclude KEEPS uncategorised (NULL) items' ([int]$nullEx -gt 0) "null_cat_items_in_exclude_sample=$nullEx"
}

# ---- 4. schedule round-trip carries the filter (the bug we just fixed) ----
$fd = @{
  scheduleName='QA_ITEMS_TEST'; recurrenceType='Daily'; scheduleTime='08:00';
  exportFormat='Excel'; recipients='gm@powersoft.com.cy'; emailSubject='QA';
  includeAiAnalysis='false'; skipIfEmpty='false';
  parametersJson = ('{"reportDateRange":{"type":"LastNDays","value":30},"reportType":"PurchasesSales","itemsSelectionJson":"' + ($incJson -replace '"','\"') + '"}');
  recurrenceJson = '{"type":"Daily","time":"08:00","range":{"startDate":"2026-06-03","noEndDate":true}}'
}
try {
  $sv = Invoke-WebRequest "$base/Reports/SavePsSchedule" -Method Post -Body $fd -WebSession $session -UseBasicParsing -TimeoutSec 60
  $svj = $sv.Content | ConvertFrom-Json
  Add-Result 'SCHEDULE: SavePsSchedule ok' ([bool]$svj.success) ("msg=" + $svj.message)
  $list = (Invoke-WebRequest "$base/Reports/GetPsSchedules" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
  $mine = $list | Where-Object { $_.scheduleName -eq 'QA_ITEMS_TEST' } | Select-Object -First 1
  if ($mine) {
    $byId = (Invoke-WebRequest "$base/Reports/GetScheduleById?scheduleId=$($mine.scheduleId)" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    $pj = [string]$byId.schedule.parametersJson
    Add-Result 'SCHEDULE: stored params carry itemsSelectionJson' ($pj -match 'itemsSelectionJson' -and $pj -match 'categories') "len=$($pj.Length)"
    # cleanup
    try { Invoke-WebRequest "$base/Reports/DeleteSchedule" -Method Post -Body @{ scheduleId=$mine.scheduleId } -WebSession $session -UseBasicParsing -TimeoutSec 30 | Out-Null } catch {}
  } else { Add-Result 'SCHEDULE: stored params carry itemsSelectionJson' $false 'schedule not found in list' }
} catch { Add-Result 'SCHEDULE: SavePsSchedule ok' $false ("EXC: " + $_.Exception.Message) }

# ---- summary ----
Write-Host ""
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$pass = ($results | Where-Object Status -eq 'PASS').Count
$fail = ($results | Where-Object Status -eq 'FAIL').Count
Write-Host "============================================"
Write-Host "ITEMS-SELECTION QA  TOTAL:$($results.Count)  PASS:$pass  FAIL:$fail"
Write-Host "============================================"
if ($fail -gt 0) { $results | Where-Object Status -eq 'FAIL' | ForEach-Object { Write-Host (" FAIL - " + $_.Test + " :: " + $_.Detail) } }

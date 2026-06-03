$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5150'
$session = $null

$login = Invoke-WebRequest "$base/Account/Login" -SessionVariable session -UseBasicParsing -TimeoutSec 60
$token = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
$body = @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token }
try { Invoke-WebRequest "$base/Account/Login" -Method Post -Body $body -WebSession $session -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 30 | Out-Null } catch {}
$conn = (Invoke-WebRequest "$base/Home/Connect" -Method Post -Body @{ databaseCode='DEMO365MODAPRO1' } -WebSession $session -UseBasicParsing -TimeoutSec 90).Content | ConvertFrom-Json
Write-Output ("CONNECT: success={0} db={1}" -f $cj.success, $conn.databaseName)

# GET CancelLog page -> must render the shared _Schedule partial
$page = Invoke-WebRequest "$base/Reports/CancelLog" -WebSession $session -UseBasicParsing -TimeoutSec 60
$c = $page.Content
function Has($needle) { if ($c -match [regex]::Escape($needle)) { 'YES' } else { 'NO' } }
Write-Output ("PAGE status={0} len={1}" -f $page.StatusCode, $c.Length)
Write-Output ("  partial: schedDateRangeType = {0}" -f (Has 'id="schedDateRangeType"'))
Write-Output ("  partial: weekly day picker  = {0}" -f (Has 'class="sched-dow"'))
Write-Output ("  partial: monthly day picker = {0}" -f (Has 'id="schedMonthDay"'))
Write-Output ("  partial: start date         = {0}" -f (Has 'id="schedStartDate"'))
Write-Output ("  host:    collectScheduleParameters = {0}" -f (Has 'function collectScheduleParameters'))
Write-Output ("  host:    onScheduleParametersLoaded= {0}" -f (Has 'function onScheduleParametersLoaded'))
Write-Output ("  no dup:  old loadSchedules() gone  = {0}" -f $(if ($c -match 'function loadSchedules\(\)') {'NO (still present!)'} else {'YES'}))
Write-Output ("  save url: SaveCancelLogSchedule    = {0}" -f (Has 'SaveCancelLogSchedule'))

# Save a Pareto schedule and read it back -> proves new param plumbing round-trips
$pareto = @{
  scheduleName = 'QA Pareto Sched'; recurrenceType='Weekly'; scheduleTime='08:00'; exportFormat='Excel';
  recipients='gm@powersoft.com.cy'; emailSubject='QA';
  parametersJson = '{"dimension":"Category","metric":"Value","includeVat":false,"excludeNegativeAmounts":true,"classAThreshold":"80","classBThreshold":"95","profitBasis":"LatestCost","timezoneOffsetMinutes":0,"reportDateRange":{"type":"LastNDays","value":30},"itemsSelectionJson":"{\"categories\":{\"ids\":[\"1\"],\"mode\":\"include\"}}"}';
  recurrenceJson = '{"type":"Weekly","time":"08:00","range":{"startDate":"2026-06-10","noEndDate":true},"pattern":{"interval":1,"daysOfWeek":[1,3]}}'
}
$ps = (Invoke-WebRequest "$base/Reports/SaveParetoSchedule" -Method Post -Body $pareto -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Write-Output ("PARETO SAVE: success={0} id={1} nextRunCalc(recurrenceJson honoured)" -f $ps.success, $ps.scheduleId)
if ($ps.scheduleId) {
  $back = (Invoke-WebRequest ("$base/Reports/GetScheduleById?scheduleId=" + $ps.scheduleId) -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
  $s = $back.schedule
  Write-Output ("  readback: nextRun={0} recurrence={1}" -f $s.nextRun, $s.recurrenceType)
  # cleanup
  $del = New-Object System.Collections.Specialized.NameValueCollection
  Invoke-WebRequest "$base/Reports/DeleteSchedule" -Method Post -Body @{ scheduleId = $ps.scheduleId } -WebSession $session -UseBasicParsing -TimeoutSec 30 | Out-Null
  Write-Output "  cleanup: deleted test schedule"
}

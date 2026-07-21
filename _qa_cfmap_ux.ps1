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

# 1) page renders with new panels
$page = Invoke-WebRequest "$base/CashFlowMapping" -WebSession $session -UseBasicParsing -TimeoutSec 60
Check 'page 200' ($page.StatusCode -eq 200)
Check 'page has coverage panel' ($page.Content -match 'Coverage of your chart of accounts')
Check 'page has range preview host' ($page.Content -match 'rangePreview')
Check 'page has delete modal' ($page.Content -match 'deleteModal')
Check 'page has table filter' ($page.Content -match 'tableFilter')
Check 'page has flash css' ($page.Content -match 'cfmFlash')

# 2) coverage endpoint
$cov = (Invoke-WebRequest "$base/CashFlowMapping/Coverage" -WebSession $session -UseBasicParsing -TimeoutSec 120).Content | ConvertFrom-Json
Check 'coverage success' ($cov.success -eq $true) $cov.message
Check 'coverage totals sane' ($cov.totalAccounts -gt 0 -and $cov.mappedAccounts -le $cov.totalAccounts)
Check 'coverage active subset' ($cov.activeAccounts -le $cov.totalAccounts -and $cov.activeMappedAccounts -le $cov.activeAccounts)
Write-Output ("  coverage: total={0} mapped={1} active={2} activeMapped={3} unassigned={4} truncated={5}" -f `
    $cov.totalAccounts, $cov.mappedAccounts, $cov.activeAccounts, $cov.activeMappedAccounts, $cov.unassigned.Count, $cov.unassignedTruncated)

# independent DB verification of coverage counts
$cn = New-Object System.Data.SqlClient.SqlConnection('Data Source=ANDREASPS\SQLDEVELOPER17PS;Initial Catalog=pswaDEMO365MODAPRO1;User ID=sa;Password=SQLADMIN123!;TrustServerCertificate=True;Encrypt=False')
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandTimeout = 120
$cmd.CommandText = @"
;WITH CashTx AS (
    SELECT DISTINCT t.fk_tt_number FROM tbl_payments t
    INNER JOIN tbl_accbank b ON t.fk_tt_accode = b.fk_ba_link
),
Active AS (
    SELECT DISTINCT t.fk_tt_accode AS Code FROM tbl_payments t
    INNER JOIN CashTx c ON t.fk_tt_number = c.fk_tt_number
    WHERE t.fk_tt_type NOT IN ('OB','YE')
),
Flags AS (
    SELECT d.pk_detailid,
           CASE WHEN EXISTS (SELECT 1 FROM dboReportsAI.tbl_CashFlowMapping m WHERE d.pk_detailid >= m.CodeFrom AND d.pk_detailid <= m.CodeTo) THEN 1 ELSE 0 END AS Mapped,
           CASE WHEN a.Code IS NOT NULL THEN 1 ELSE 0 END AS IsActive
    FROM tbl_detailac d LEFT JOIN Active a ON a.Code = d.pk_detailid
)
SELECT COUNT(*), SUM(Mapped), SUM(IsActive) FROM Flags
"@
$rd = $cmd.ExecuteReader(); $rd.Read() | Out-Null
$dbTotal = $rd.GetValue(0); $dbMapped = $rd.GetValue(1); $dbActive = $rd.GetValue(2)
$rd.Close(); $cn.Close()
Check 'coverage total == DB' ($cov.totalAccounts -eq $dbTotal) ("api={0} db={1}" -f $cov.totalAccounts, $dbTotal)
Check 'coverage mapped == DB' ($cov.mappedAccounts -eq $dbMapped) ("api={0} db={1}" -f $cov.mappedAccounts, $dbMapped)
Check 'coverage active == DB' ($cov.activeAccounts -eq $dbActive) ("api={0} db={1}" -f $cov.activeAccounts, $dbActive)

# unassigned list: active first, none of them should resolve
if ($cov.unassigned.Count -gt 0) {
    $firstInactiveIdx = -1; $lastActiveIdx = -1; $i = 0
    foreach ($u in $cov.unassigned) {
        if ($u.active) { $lastActiveIdx = $i } elseif ($firstInactiveIdx -lt 0) { $firstInactiveIdx = $i }
        $i++
    }
    Check 'unassigned sorted active-first' ($firstInactiveIdx -lt 0 -or $lastActiveIdx -lt $firstInactiveIdx)
    $probe = $cov.unassigned[0].code
    $r = (Invoke-WebRequest ("$base/CashFlowMapping/Resolve?accountCode=" + [uri]::EscapeDataString($probe)) -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    Check 'unassigned account really unresolved' ($r.success -eq $true -and $r.matched -eq $false) ("code={0}" -f $probe)
}

# 3) preview endpoint
$p = (Invoke-WebRequest "$base/CashFlowMapping/PreviewRange?codeFrom=124002&codeTo=124004" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Check 'preview success' ($p.success -eq $true) $p.message
Check 'preview finds accounts' ($p.matchCount -ge 1)
Check 'preview sample <= count' ($p.sample.Count -le $p.matchCount)
Check 'preview reports overlap (range exists in mapping)' ($p.overlaps.Count -ge 1)
Write-Output ("  preview 124002-124004: count={0} sample={1} overlaps={2}" -f $p.matchCount, $p.sample.Count, $p.overlaps.Count)

# preview excludeId hides the row being edited
$list = (Invoke-WebRequest "$base/CashFlowMapping/List" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
$own = $list.rows | Where-Object { $_.codeFrom -eq '124002' -and $_.codeTo -eq '124004' } | Select-Object -First 1
if ($own) {
    $p2 = (Invoke-WebRequest ("$base/CashFlowMapping/PreviewRange?codeFrom=124002&codeTo=124004&excludeId=" + $own.id) -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    Check 'preview excludeId drops own row' ($p2.overlaps.Count -eq ($p.overlaps.Count - 1)) ("with={0} without={1}" -f $p.overlaps.Count, $p2.overlaps.Count)
}

# invalid ranges rejected
$bad = (Invoke-WebRequest "$base/CashFlowMapping/PreviewRange?codeFrom=9&codeTo=1" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Check 'preview rejects From>To' ($bad.success -eq $false)
$empty = (Invoke-WebRequest "$base/CashFlowMapping/PreviewRange?codeFrom=&codeTo=5" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Check 'preview rejects empty From' ($empty.success -eq $false)

# preview of a range with no accounts
$none = (Invoke-WebRequest "$base/CashFlowMapping/PreviewRange?codeFrom=ZZZZ&codeTo=ZZZZ9" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Check 'preview empty range count=0' ($none.success -eq $true -and $none.matchCount -eq 0)

Write-Output ''
Write-Output ("RESULT: {0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }

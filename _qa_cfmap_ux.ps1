$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5150'
$login = Invoke-WebRequest "$base/Account/Login" -SessionVariable session -UseBasicParsing -TimeoutSec 60
$token = [regex]::Match($login.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
try { Invoke-WebRequest "$base/Account/Login" -Method Post -Body @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token } -WebSession $session -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 30 | Out-Null } catch {}
Invoke-WebRequest "$base/Home/Connect" -Method Post -Body @{ databaseCode='DEMO365MODAPRO1' } -WebSession $session -UseBasicParsing -TimeoutSec 90 | Out-Null

$page = Invoke-WebRequest "$base/CashFlowMapping" -WebSession $session -UseBasicParsing -TimeoutSec 60
Write-Output ("PAGE: status={0} len={1} title-ok={2}" -f $page.StatusCode, $page.Content.Length, ($page.Content -match 'Cash Flow Mapping'))

$list = (Invoke-WebRequest "$base/CashFlowMapping/List" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
Write-Output ("LIST: success={0} rows={1}" -f $list.success, $list.rows.Count)
$groups = $list.rows | Group-Object groupName
Write-Output ("GROUPS: {0}" -f $groups.Count)
$groups | ForEach-Object { Write-Output ("  [{0}] {1} rows (sort={2})" -f $_.Name, $_.Count, ($_.Group[0].groupSortOrder)) }

# overlap analysis: how many account-space overlaps exist in the default mapping?
$rows = $list.rows
$overlaps = 0
for ($i = 0; $i -lt $rows.Count; $i++) {
    for ($j = $i + 1; $j -lt $rows.Count; $j++) {
        $a = $rows[$i]; $b = $rows[$j]
        if (([string]::CompareOrdinal($a.codeFrom, $b.codeTo) -le 0) -and ([string]::CompareOrdinal($b.codeFrom, $a.codeTo) -le 0)) { $overlaps++ }
    }
}
Write-Output ("OVERLAPPING RANGE PAIRS in default mapping: {0}" -f $overlaps)

# resolve a few codes
foreach ($code in @('124002','380','999999','1')) {
    $r = (Invoke-WebRequest ("$base/CashFlowMapping/Resolve?accountCode=" + $code) -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
    Write-Output ("RESOLVE {0}: matched={1} -> {2} / {3} via {4}-{5} (matches={6})" -f $code, $r.matched, $r.groupName, $r.categoryName, $r.codeFrom, $r.codeTo, $r.matchCount)
}

# does the tenant have real COA accounts to compare coverage against?
$cn = New-Object System.Data.SqlClient.SqlConnection('Data Source=ANDREASPS\SQLDEVELOPER17PS;Initial Catalog=pswaDEMO365MODAPRO1;User ID=sa;Password=SQLADMIN123!;TrustServerCertificate=True;Encrypt=False')
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandText = "SELECT COUNT(*) FROM tbl_detailac"
Write-Output ("tbl_detailac accounts: {0}" -f $cmd.ExecuteScalar())
$cmd.CommandText = "SELECT TOP 5 pk_detailid FROM tbl_detailac ORDER BY pk_detailid"
$rd = $cmd.ExecuteReader()
$codes = @(); while ($rd.Read()) { $codes += $rd.GetValue(0) }
$rd.Close(); $cn.Close()
Write-Output ("sample account ids: {0}" -f ($codes -join ', '))

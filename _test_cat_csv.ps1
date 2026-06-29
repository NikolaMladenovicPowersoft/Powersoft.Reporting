$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5150'
$cookiePath = 'C:\p\Powersoft.Reporting\_test_cookies.json'
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$cookies = Get-Content $cookiePath -Raw | ConvertFrom-Json
foreach ($c in $cookies) {
    $cookie = New-Object System.Net.Cookie($c.Name, $c.Value, $c.Path, $c.Domain)
    $session.Cookies.Add($(New-Object System.Uri($base)), $cookie)
}

$csvUrl = "$base/Reports/ExportCatalogueCsv?dateFrom=2025-01-01&dateTo=2025-12-31&reportMode=Summary&reportOn=Sale&primaryGroup=Category&secondaryGroup=None&thirdGroup=None&dateBasis=DateTrans&includeVat=false&showProfit=true&showStock=true&sortColumn=GroupName&sortDirection=ASC"
$resp = Invoke-WebRequest -Uri $csvUrl -UseBasicParsing -WebSession $session -OutFile "C:\p\Powersoft.Reporting\_test_cat_data.csv"
Write-Host "File saved"
$content = Get-Content "C:\p\Powersoft.Reporting\_test_cat_data.csv" -Head 15
$content | ForEach-Object { Write-Host $_ }
Write-Host "`n--- GRAND TOTAL ---"
Get-Content "C:\p\Powersoft.Reporting\_test_cat_data.csv" -Tail 3 | ForEach-Object { Write-Host $_ }

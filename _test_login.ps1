$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5150'
$cookiePath = 'C:\p\Powersoft.Reporting\_test_cookies.json'

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# GET login page
$loginPage = Invoke-WebRequest -Uri "$base/Account/Login" -UseBasicParsing -WebSession $session
$token = ([regex]::Match($loginPage.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
Write-Host "Got antiforgery token"

# POST login
$body = "Username=REPORTING_TEST&Password=Test123!&RememberMe=true&__RequestVerificationToken=$([uri]::EscapeDataString($token))"
$resp = Invoke-WebRequest -Uri "$base/Account/Login" -Method POST -Body $body -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -WebSession $session
$title = ([regex]::Match($resp.Content, '<title>([^<]+)</title>')).Groups[1].Value
Write-Host "After login title: $title"

# Check cookies
$allCookies = $session.Cookies.GetCookies($base)
Write-Host "Cookie count: $($allCookies.Count)"
foreach ($c in $allCookies) { Write-Host "  $($c.Name)" }

# Save cookies
$cookieList = @()
foreach ($c in $allCookies) {
    $cookieList += @{ Name = $c.Name; Value = $c.Value; Domain = $c.Domain; Path = $c.Path }
}
$cookieList | ConvertTo-Json -Depth 3 | Out-File $cookiePath -Encoding utf8
Write-Host "Cookies saved to $cookiePath"

# Save the DB selection page for analysis
$resp.Content | Out-File 'C:\p\Powersoft.Reporting\_test_homepage.html' -Encoding utf8
Write-Host "Homepage saved"

# Show companies/databases
$resp.Content -split "`n" | Where-Object { $_ -match 'company|database|connect|card-title|card-body|btn-primary|h[2-5]>' -and $_ -notmatch 'css|bootstrap|cdn|script src|font|linear-gradient|:root|placeholder' } | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 5 } | Select-Object -First 30

param(
    [string]$Action = "login",
    [string]$BaseUrl = "http://localhost:5150",
    [string]$CookieFile = "C:\p\Powersoft.Reporting\_test_cookies.json"
)

$ErrorActionPreference = "Continue"

function Save-Cookies($session, $url, $path) {
    $cookies = @()
    foreach ($c in $session.Cookies.GetCookies($url)) {
        $cookies += @{ Name = $c.Name; Value = $c.Value; Domain = $c.Domain; Path = $c.Path }
    }
    $cookies | ConvertTo-Json | Out-File $path -Encoding utf8
}

function Load-Session($url, $path) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    if (Test-Path $path) {
        $cookies = Get-Content $path -Raw | ConvertFrom-Json
        foreach ($c in $cookies) {
            $cookie = New-Object System.Net.Cookie($c.Name, $c.Value, $c.Path, $c.Domain)
            $session.Cookies.Add($cookie)
        }
    }
    return $session
}

function Do-Login {
    Write-Host "=== LOGIN ==="
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    
    $loginPage = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -UseBasicParsing -WebSession $session
    $token = ([regex]::Match($loginPage.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    
    $body = "Username=REPORTING_TEST&Password=Test123!&RememberMe=false&__RequestVerificationToken=$([uri]::EscapeDataString($token))"
    $resp = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -Method POST -Body $body -ContentType "application/x-www-form-urlencoded" -UseBasicParsing -WebSession $session
    
    $title = ([regex]::Match($resp.Content, '<title>([^<]+)</title>')).Groups[1].Value
    Write-Host "After login - Title: $title"
    
    $cookieNames = ($session.Cookies.GetCookies($BaseUrl) | ForEach-Object { $_.Name }) -join ", "
    Write-Host "Cookies: $cookieNames"
    
    if ($title -match "Login") {
        Write-Host "ERROR: Login failed - still on login page"
        return
    }
    
    Save-Cookies $session $BaseUrl $CookieFile
    Write-Host "Session saved."
    
    # Try to find databases
    Do-SelectDB $session
}

function Do-SelectDB($session) {
    Write-Host "`n=== DATABASE SELECTION ==="
    if (-not $session) { $session = Load-Session $BaseUrl $CookieFile }
    
    $resp = Invoke-WebRequest -Uri "$BaseUrl/Home/Index" -UseBasicParsing -WebSession $session
    $title = ([regex]::Match($resp.Content, '<title>([^<]+)</title>')).Groups[1].Value
    Write-Host "Page: $title"
    
    # Save raw for debugging
    $resp.Content | Out-File "C:\p\Powersoft.Reporting\_test_homepage.html" -Encoding utf8
    
    # Find company cards / database links
    $companyMatches = [regex]::Matches($resp.Content, 'selectCompany\([''"]([^''"]+)[''"]\)')
    $dbMatches = [regex]::Matches($resp.Content, 'connectToDatabase\([''"]([^''"]+)[''"]\)')
    $connectMatches = [regex]::Matches($resp.Content, 'connect[^"]*\([''"]([^''"]+)[''"]\)')
    
    Write-Host "Companies: $($companyMatches.Count)"
    foreach ($m in $companyMatches) { Write-Host "  $($m.Groups[1].Value)" }
    
    Write-Host "DBs: $($dbMatches.Count)"
    foreach ($m in $dbMatches) { Write-Host "  $($m.Groups[1].Value)" }
    
    Write-Host "Connect calls: $($connectMatches.Count)"
    foreach ($m in $connectMatches) { Write-Host "  $($m.Groups[0].Value)" }
    
    # Also list forms
    $formMatches = [regex]::Matches($resp.Content, '<form[^>]+>')
    Write-Host "Forms: $($formMatches.Count)"
    foreach ($m in $formMatches) { Write-Host "  $($m.Value)" }
    
    Save-Cookies $session $BaseUrl $CookieFile
}

function Do-Connect($session, $dbCode) {
    Write-Host "`n=== CONNECTING TO DB: $dbCode ==="
    if (-not $session) { $session = Load-Session $BaseUrl $CookieFile }
    
    $resp = Invoke-WebRequest -Uri "$BaseUrl/Home/Connect" -Method POST -Body (@{databaseCode=$dbCode} | ConvertTo-Json) -ContentType "application/json" -UseBasicParsing -WebSession $session
    Write-Host "Status: $($resp.StatusCode)"
    Write-Host "Response: $($resp.Content)"
    
    Save-Cookies $session $BaseUrl $CookieFile
}

function Do-TestAB($session) {
    Write-Host "`n=== TESTING AVERAGE BASKET ==="
    if (-not $session) { $session = Load-Session $BaseUrl $CookieFile }
    
    # 1. Page Load
    Write-Host "`n--- 1. Page Load ---"
    $resp = Invoke-WebRequest -Uri "$BaseUrl/Reports/AverageBasket" -UseBasicParsing -WebSession $session
    $title = ([regex]::Match($resp.Content, '<title>([^<]+)</title>')).Groups[1].Value
    Write-Host "Title: $title"
    Write-Host "Size: $($resp.Content.Length) bytes"
    
    if ($resp.Content -match "Login") {
        Write-Host "ERROR: Redirected to login — not authenticated"
        return
    }
    
    # Check for key UI elements
    $hasDateFrom = $resp.Content -match 'id="dateFrom"'
    $hasDateTo = $resp.Content -match 'id="dateTo"'
    $hasBreakdown = $resp.Content -match 'id="breakdown"' -or $resp.Content -match 'name="Breakdown"'
    $hasGroupBy = $resp.Content -match 'id="groupBy"' -or $resp.Content -match 'name="GroupBy"'
    $hasGenerate = $resp.Content -match 'Generate|btnGenerate|generateReport'
    
    Write-Host "DateFrom input: $hasDateFrom"
    Write-Host "DateTo input: $hasDateTo"
    Write-Host "Breakdown select: $hasBreakdown"
    Write-Host "GroupBy select: $hasGroupBy"
    Write-Host "Generate button: $hasGenerate"
    
    $resp.Content | Out-File "C:\p\Powersoft.Reporting\_test_ab_page.html" -Encoding utf8
    Write-Host "Page saved to _test_ab_page.html"
    
    Save-Cookies $session $BaseUrl $CookieFile
}

# Execute
switch ($Action) {
    "login" { Do-Login }
    "selectdb" { Do-SelectDB }
    "connect" { 
        $session = Load-Session $BaseUrl $CookieFile
        Do-Connect $session $args[0]
    }
    "testab" { Do-TestAB }
}

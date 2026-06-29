$ErrorActionPreference = 'Continue'
$base = "http://localhost:5150"
$pass = 0; $fail = 0; $results = @()

function Log-Result($cat, $name, $ok, $detail) {
    $script:results += [PSCustomObject]@{ Category=$cat; Test=$name; Status=if($ok){'PASS'}else{'FAIL'}; Detail=$detail }
    if ($ok) { $script:pass++; Write-Host "  PASS: [$cat] $name" -ForegroundColor Green }
    else { $script:fail++; Write-Host "  FAIL: [$cat] $name - $detail" -ForegroundColor Red }
}

function Test-Page($cat, $name, $url, $method, $body, $expectInContent, $session) {
    try {
        $params = @{ Uri="$base$url"; WebSession=$session; UseBasicParsing=$true; MaximumRedirection=5 }
        if ($method -eq 'POST') { $params['Method'] = 'POST'; $params['Body'] = $body }
        $r = Invoke-WebRequest @params
        $ok = $r.StatusCode -eq 200
        $contentOk = $true
        if ($expectInContent) {
            $contentOk = $r.Content -match $expectInContent
            $ok = $ok -and $contentOk
        }
        $isLogin = $r.Content -match 'Sign in to continue'
        if ($isLogin -and $url -ne '/Account/Login') { $ok = $false; $detail = "REDIRECTED TO LOGIN" }
        else { $detail = "Status=$($r.StatusCode), Size=$($r.Content.Length)" }
        if (-not $contentOk) { $detail += ", MISSING: $expectInContent" }
        Log-Result $cat $name $ok $detail
        return $r
    } catch {
        Log-Result $cat $name $false "Error: $($_.Exception.Message)"
        return $null
    }
}

function Test-Json($cat, $name, $url, $method, $body, $session) {
    try {
        $params = @{ Uri="$base$url"; WebSession=$session; UseBasicParsing=$true; MaximumRedirection=5 }
        if ($method -eq 'POST') { $params['Method'] = 'POST'; $params['Body'] = $body }
        $r = Invoke-WebRequest @params
        $isLogin = $r.Content -match 'Sign in to continue'
        if ($isLogin) { Log-Result $cat $name $false "REDIRECTED TO LOGIN"; return $null }
        $json = $r.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
        $ok = $r.StatusCode -eq 200 -and $json -ne $null
        Log-Result $cat $name $ok "Status=$($r.StatusCode), Size=$($r.Content.Length)"
        return $json
    } catch {
        Log-Result $cat $name $false "Error: $($_.Exception.Message)"
        return $null
    }
}

function Test-Export($cat, $name, $url, $expectedType, $session) {
    try {
        $r = Invoke-WebRequest -Uri "$base$url" -WebSession $session -UseBasicParsing
        $ct = $r.Headers['Content-Type']
        $cd = $r.Headers['Content-Disposition']
        $ok = ($ct -match $expectedType -or $cd -match $expectedType) -and $r.Content.Length -gt 100
        $isLogin = $false
        if ($r.Content.Length -lt 10000) {
            try { $txt = [System.Text.Encoding]::UTF8.GetString($r.Content); $isLogin = $txt -match 'Sign in' } catch {}
        }
        if ($isLogin) { $ok = $false }
        Log-Result $cat $name $ok "ContentType=$ct, Size=$($r.Content.Length)"
    } catch {
        Log-Result $cat $name $false "Error: $($_.Exception.Message)"
    }
}

# ====================================================================
# SECTION 1: LOGIN + CONNECT
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 1: AUTHENTICATION & DB CONNECTION" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# Login
$loginPage = Invoke-WebRequest -Uri "$base/Account/Login" -UseBasicParsing -SessionVariable sess
$token = ''; if ($loginPage.Content -match '__RequestVerificationToken.*?value="([^"]+)"') { $token = $Matches[1] }
Log-Result "Auth" "Login page loads" ($loginPage.StatusCode -eq 200) "Status=$($loginPage.StatusCode)"

$loginBody = @{ Username='REPORTING_TEST'; Password='Test123!'; __RequestVerificationToken=$token }
$loginResp = Invoke-WebRequest -Uri "$base/Account/Login" -Method POST -Body $loginBody -WebSession $sess -UseBasicParsing -MaximumRedirection 5
$loginOk = $loginResp.StatusCode -eq 200 -and -not ($loginResp.Content -match 'Invalid username')
Log-Result "Auth" "Login POST succeeds" $loginOk "Status=$($loginResp.StatusCode)"

# DB Selection page
$dbPage = Test-Page "Auth" "DB Selection page loads" "/Home/Index" "GET" $null "Select Database|select.*company" $sess

# Get databases JSON
$dbJson = Test-Json "Auth" "GetDatabases API" "/Home/GetDatabases?companyCode=DEMOS" "GET" $null $sess

# Connect to DB
$connectResp = Invoke-WebRequest -Uri "$base/Home/Connect" -Method POST -Body @{databaseCode='DEMO365MODAPRO1'} -WebSession $sess -UseBasicParsing -MaximumRedirection 5
$connectOk = $connectResp.StatusCode -eq 200 -and -not ($connectResp.Content -match 'Sign in to continue')
Log-Result "Auth" "Connect to DB" $connectOk "Status=$($connectResp.StatusCode)"

# Access Denied page
Test-Page "Auth" "AccessDenied page" "/Account/AccessDenied" "GET" $null "Access Denied|denied|not authorized" $sess | Out-Null

# Privacy page  
Test-Page "Auth" "Privacy page" "/Home/Privacy" "GET" $null $null $sess | Out-Null


# ====================================================================
# SECTION 2: REPORTS DASHBOARD
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 2: REPORTS DASHBOARD" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

$dashboard = Test-Page "Dashboard" "Reports Index loads" "/Reports/Index" "GET" $null "Average Basket|Purchases|Charts" $sess

# Check all report cards exist
$reportCards = @('Average Basket','Purchases','Charts','Pareto','Catalogue','Below Min','Cancel','Prospect','Offers','Trial Balance','Profit')
foreach ($card in $reportCards) {
    $found = $dashboard.Content -match $card
    Log-Result "Dashboard" "Card: $card" $found ""
}


# ====================================================================
# SECTION 3: ALL REPORT PAGES LOAD
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 3: ALL REPORT PAGES LOAD" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

$reportPages = @(
    @{Name='Average Basket'; Url='/Reports/AverageBasket'; Check='Breakdown|Date From'},
    @{Name='Purchases vs Sales'; Url='/Reports/PurchasesSales'; Check='Report Mode|Primary Group'},
    @{Name='Charts'; Url='/Reports/Charts'; Check='Chart Type|Dimension'},
    @{Name='Pareto 80/20'; Url='/Reports/Pareto'; Check='Pareto|Analysis'},
    @{Name='Catalogue'; Url='/Reports/Catalogue'; Check='Catalogue|Category'},
    @{Name='Below Min Stock'; Url='/Reports/BelowMinStock'; Check='Below|Minimum|Stock'},
    @{Name='Cancel Log'; Url='/Reports/CancelLog'; Check='Cancel|Action Type'},
    @{Name='Prospect Clients'; Url='/Reports/ProspectClients'; Check='Prospect|Client'},
    @{Name='Offers Report'; Url='/Reports/OffersReport'; Check='Offer'},
    @{Name='Trial Balance'; Url='/Reports/TrialBalance'; Check='Trial Balance|Account'},
    @{Name='Profit Loss'; Url='/Reports/ProfitLoss'; Check='Profit|Loss'}
)
foreach ($rp in $reportPages) {
    Test-Page "Reports" "$($rp.Name) page" $rp.Url "GET" $null $rp.Check $sess | Out-Null
}


# ====================================================================
# SECTION 4: SETTINGS PAGES (ADMIN)
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 4: SETTINGS PAGES (ADMIN)" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# Database Settings
$dbSettings = Test-Page "Settings" "Database Settings page" "/Settings/Database" "GET" $null "Schedule|Retention|Export Format|Token" $sess

# System Settings (ranking < 15 only)
$sysSettings = Test-Page "Settings" "System Settings page" "/Settings/System" "GET" $null "Scheduler|SMTP|Global|Maximum" $sess

# Email Templates list
$emailTemplates = Test-Page "Settings" "Email Templates page" "/Settings/EmailTemplates" "GET" $null "Email Template|template" $sess

# AI Usage (ranking == 1 only)
$aiUsage = Test-Page "Settings" "AI Usage Report page" "/Settings/AiUsage" "GET" $null "AI Usage|usage|token" $sess

# Edit Email Template (new)
$editTemplate = Test-Page "Settings" "Edit Email Template (new)" "/Settings/EditEmailTemplate" "GET" $null "Template Name|Subject|Body" $sess


# ====================================================================
# SECTION 5: SCHEDULE MANAGEMENT
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 5: SCHEDULE MANAGEMENT" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# All Schedules page
Test-Page "Schedule" "All Schedules page" "/Reports/AllSchedules" "GET" $null "Schedule|All Schedules" $sess | Out-Null

# Schedule Logs page
Test-Page "Schedule" "Schedule Logs page" "/Reports/ScheduleLogs" "GET" $null "Schedule Logs|Log" $sess | Out-Null

# Get All Schedules API
$allScheds = Test-Json "Schedule" "GetAllSchedules API" "/Reports/GetAllSchedules" "GET" $null $sess

# Get Schedule Logs API
$logs = Test-Json "Schedule" "GetScheduleLogs API" "/Reports/GetScheduleLogs" "GET" $null $sess

# Per-report schedule APIs
$schedEndpoints = @(
    @{Name='AB Schedules'; Url='/Reports/GetSchedules'},
    @{Name='PS Schedules'; Url='/Reports/GetPsSchedules'},
    @{Name='Pareto Schedules'; Url='/Reports/GetParetoSchedules'},
    @{Name='Chart Schedules'; Url='/Reports/GetChartSchedules'},
    @{Name='Catalogue Schedules'; Url='/Reports/GetCatalogueSchedules'},
    @{Name='BMS Schedules'; Url='/Reports/GetBmsSchedules'},
    @{Name='CancelLog Schedules'; Url='/Reports/GetCancelLogSchedules'},
    @{Name='TrialBalance Schedules'; Url='/Reports/GetTrialBalanceSchedules'},
    @{Name='ProfitLoss Schedules'; Url='/Reports/GetProfitLossSchedules'},
    @{Name='ProspectClients Schedules'; Url='/Reports/GetProspectClientsSchedules'},
    @{Name='OffersReport Schedules'; Url='/Reports/GetOffersReportSchedules'}
)
foreach ($se in $schedEndpoints) {
    Test-Json "Schedule" $se.Name $se.Url "GET" $null $sess | Out-Null
}


# ====================================================================
# SECTION 6: EMAIL TEMPLATE CRUD
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 6: EMAIL TEMPLATE CRUD" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# List templates via reports API
$templates = Test-Json "Email" "GetEmailTemplates API" "/Reports/GetEmailTemplates" "GET" $null $sess

# List with report type filter
$templatesFiltered = Test-Json "Email" "GetEmailTemplates (AverageBasket)" "/Reports/GetEmailTemplates?reportType=AverageBasket" "GET" $null $sess

# Get email recipients
$recipients = Test-Json "Email" "GetEmailRecipients API" "/Reports/GetEmailRecipients" "GET" $null $sess


# ====================================================================
# SECTION 7: SHARED API ENDPOINTS
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 7: SHARED API ENDPOINTS" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# Stores
$stores = Test-Json "API" "GetStores" "/Reports/GetStores" "GET" $null $sess

# Dimensions
$dimensions = @('Category','Department','Brand','Season','Supplier','Model','Colour','SizeGroup','Fabric')
foreach ($dim in $dimensions) {
    Test-Json "API" "GetDimensions ($dim)" "/Reports/GetDimensions?type=$dim" "GET" $null $sess | Out-Null
}

# Search Items
Test-Json "API" "SearchItems" "/Reports/SearchItems?q=test" "GET" $null $sess | Out-Null

# AI Status
Test-Json "API" "GetAiStatus" "/Reports/GetAiStatus" "GET" $null $sess | Out-Null

# Token Budget
Test-Json "API" "GetTokenBudget" "/Reports/GetTokenBudget" "GET" $null $sess | Out-Null

# Database Users
Test-Json "API" "GetDatabaseUsers" "/Reports/GetDatabaseUsers" "GET" $null $sess | Out-Null

# Report Layout
Test-Json "API" "GetReportLayout (AB)" "/Reports/GetReportLayout?reportType=AverageBasket" "GET" $null $sess | Out-Null
Test-Json "API" "GetReportLayout (PS)" "/Reports/GetReportLayout?reportType=PurchasesSales" "GET" $null $sess | Out-Null

# List Report Layouts
Test-Json "API" "ListReportLayouts (AB)" "/Reports/ListReportLayouts?reportType=AverageBasket" "GET" $null $sess | Out-Null

# Filter Presets
Test-Json "API" "GetFilterPresets (AB)" "/Reports/GetFilterPresets?reportType=AverageBasket" "GET" $null $sess | Out-Null

# AI Prompt Templates
Test-Json "API" "GetAiPromptTemplates" "/Reports/GetAiPromptTemplates" "GET" $null $sess | Out-Null

# Entity Detail (item)
Test-Json "API" "GetEntityDetail (Item)" "/Reports/GetEntityDetail?type=item&code=0020" "GET" $null $sess | Out-Null


# ====================================================================
# SECTION 8: DATABASE SETTINGS FUNCTIONALITY
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 8: DATABASE SETTINGS FUNCTIONALITY" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

if ($dbSettings) {
    # Check all expected form elements
    $dbSettingsChecks = @(
        @{Name='Max Schedules field'; Check='MaxSchedules|maxSchedules'},
        @{Name='Export Format field'; Check='ExportFormat|exportFormat'},
        @{Name='Scheduler Enabled toggle'; Check='SchedulerEnabled|schedulerEnabled'},
        @{Name='Retention Days field'; Check='RetentionDays|retentionDays'},
        @{Name='AI Token Budget'; Check='TokenBudget|tokenBudget|AiTokenBudget'}
    )
    foreach ($chk in $dbSettingsChecks) {
        $found = $dbSettings.Content -match $chk.Check
        Log-Result "Settings" "DB Settings: $($chk.Name)" $found ""
    }
}

# System Settings form elements
if ($sysSettings) {
    $sysChecks = @(
        @{Name='Global Scheduler Master'; Check='SchedulerMaster|schedulerMaster|GlobalScheduler'},
        @{Name='Max DBs per run'; Check='MaxDbsPerRun|maxDbsPerRun|MaxDatabases'},
        @{Name='Global Schedule Limit'; Check='GlobalScheduleLimit|globalScheduleLimit|MaxSchedules'},
        @{Name='SMTP Host'; Check='SmtpHost|smtpHost|smtp'},
        @{Name='AI Cost Markup'; Check='AiCostMarkup|aiCostMarkup|CostMarkup'}
    )
    foreach ($chk in $sysChecks) {
        $found = $sysSettings.Content -match $chk.Check
        Log-Result "Settings" "System Settings: $($chk.Name)" $found ""
    }
}


# ====================================================================
# SECTION 9: PRINT PREVIEWS (ALL REPORTS)
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 9: PRINT PREVIEWS (ALL REPORTS)" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

$dateParams = "dateFrom=2024-01-01&dateTo=2024-12-31"

$printPreviews = @(
    @{Name='AB Print Preview'; Url="/Reports/PrintPreview?$dateParams&breakdown=Monthly&groupBy=None&includeVat=false"},
    @{Name='PS Print Preview'; Url="/Reports/PrintPsPreview?$dateParams&reportMode=0&primaryGroup=1&secondaryGroup=0&thirdGroup=0&includeVat=false&showProfit=false&showStock=false"},
    @{Name='Charts Print Preview'; Url="/Reports/PrintChartPreview?$dateParams&dimension=0&metric=0&topN=10&showOthers=true"},
    @{Name='CancelLog Print Preview'; Url="/Reports/CancelLogPrintPreview?$dateParams&reportByDateTime=false&actionType=All&reportType=Detailed"},
    @{Name='TrialBalance Print Preview'; Url="/Reports/TrialBalancePrintPreview?$dateParams"},
    @{Name='ProfitLoss Print Preview'; Url="/Reports/ProfitLossPrintPreview?$dateParams"},
    @{Name='ProspectClients Print Preview'; Url="/Reports/ProspectClientsPrintPreview?$dateParams"}
)
foreach ($pp in $printPreviews) {
    Test-Page "Print" $pp.Name $pp.Url "GET" $null $null $sess | Out-Null
}


# ====================================================================
# SECTION 10: DISCONNECTION & RECONNECTION
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 10: SESSION MANAGEMENT" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# Disconnect
try {
    $disconnResp = Invoke-WebRequest -Uri "$base/Home/Disconnect" -Method POST -WebSession $sess -UseBasicParsing -MaximumRedirection 5
    Log-Result "Session" "Disconnect" ($disconnResp.StatusCode -eq 200) "Status=$($disconnResp.StatusCode)"
} catch {
    Log-Result "Session" "Disconnect" $false "Error: $($_.Exception.Message)"
}

# After disconnect, report page should redirect
try {
    $afterDisc = Invoke-WebRequest -Uri "$base/Reports/Index" -WebSession $sess -UseBasicParsing -MaximumRedirection 5
    $redirectedHome = $afterDisc.Content -match 'Select Database|select.*company|Connect'
    Log-Result "Session" "After disconnect redirects to DB selection" $redirectedHome ""
} catch {
    Log-Result "Session" "After disconnect redirects" $false "Error: $($_.Exception.Message)"
}

# Reconnect for next tests
Invoke-WebRequest -Uri "$base/Home/Connect" -Method POST -Body @{databaseCode='DEMO365MODAPRO1'} -WebSession $sess -UseBasicParsing -MaximumRedirection 5 | Out-Null


# ====================================================================
# SECTION 11: DATA GENERATION (verify data returns)
# ====================================================================
Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " SECTION 11: REPORT DATA GENERATION" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# Pareto data
$paretoData = Test-Json "Generate" "Pareto GetData" "/Reports/GetParetoData" "POST" @{dateFrom='2024-01-01';dateTo='2024-12-31';groupBy='Category';metric='Value';topN=20;showOthers='true'} $sess
if ($paretoData) { Log-Result "Generate" "Pareto has data" ($paretoData.success -eq $true) "Rows=$($paretoData.data.Count)" }

# Charts data
$chartData = Test-Json "Generate" "Charts GetData" "/Reports/GetChartData" "POST" @{dateFrom='2024-01-01';dateTo='2024-12-31';mode='SalesAnalysis';dimension='Category';metric='Value';topN=10;showOthers='true';includeVat='false'} $sess
if ($chartData) { Log-Result "Generate" "Charts has data" ($chartData.success -eq $true) "Points=$($chartData.data.Count)" }

# BMS data
$bmsData = Test-Json "Generate" "BMS GetData" "/Reports/GetBelowMinStockData" "POST" @{sortColumn='ItemCode';sortDirection='ASC'} $sess
if ($bmsData) { Log-Result "Generate" "BMS has data" ($bmsData.success -eq $true) "Rows=$($bmsData.totalRows)" }

# CancelLog data
$clData = Test-Json "Generate" "CancelLog GetData" "/Reports/GetCancelLogData" "POST" @{dateFrom='2024-01-01';dateTo='2024-12-31';reportByDateTime='false';actionType='All';reportType='Detailed';primaryGroup='NONE';secondaryGroup='NONE'} $sess

# ProspectClients lookups
$pcLookups = Test-Json "Generate" "ProspectClients Lookups" "/Reports/GetProspectClientsLookups" "GET" $null $sess

# ProspectClients data
$pcData = Test-Json "Generate" "ProspectClients GetData" "/Reports/GetProspectClientsData" "POST" @{dateFrom='2024-01-01';dateTo='2024-12-31';sortColumn='ClientName';sortDirection='ASC'} $sess

# OffersReport lookups
$ofLookups = Test-Json "Generate" "OffersReport Lookups" "/Reports/GetOffersReportLookups" "GET" $null $sess

# OffersReport data
$ofData = Test-Json "Generate" "OffersReport GetData" "/Reports/GetOffersReportData" "POST" @{dateFrom='2024-01-01';dateTo='2024-12-31';sortColumn='OfferNumber';sortDirection='ASC'} $sess

# TrialBalance data
$tbData = Test-Json "Generate" "TrialBalance GetData" "/Reports/GetTrialBalanceData" "POST" @{dateFrom='2024-01-01';dateTo='2024-12-31'} $sess

# ProfitLoss data
$plData = Test-Json "Generate" "ProfitLoss GetData" "/Reports/GetProfitLossData" "POST" @{dateFrom='2024-01-01';dateTo='2024-12-31'} $sess

# TrialBalance account lookup
$tbAcct = Test-Json "Generate" "TrialBalance Account Lookup" "/Reports/GetTrialBalanceAccounts?q=sales" "GET" $null $sess


# ====================================================================
# SUMMARY
# ====================================================================
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "  FULL APP E2E TEST: $pass PASS / $fail FAIL / $($pass+$fail) TOTAL" -ForegroundColor $(if($fail -eq 0){'Green'}else{if($fail -le 3){'Yellow'}else{'Red'}})
Write-Host "========================================================" -ForegroundColor Cyan

if ($fail -gt 0) {
    Write-Host "`nFailed tests:" -ForegroundColor Red
    $results | Where-Object { $_.Status -eq 'FAIL' } | ForEach-Object {
        Write-Host "  [$($_.Category)] $($_.Test): $($_.Detail)" -ForegroundColor Red
    }
}

Write-Host "`nResults by category:" -ForegroundColor Gray
$results | Group-Object Category | ForEach-Object {
    $catPass = ($_.Group | Where-Object { $_.Status -eq 'PASS' }).Count
    $catFail = ($_.Group | Where-Object { $_.Status -eq 'FAIL' }).Count
    $color = if($catFail -eq 0){'Green'}else{'Yellow'}
    Write-Host "  $($_.Name): $catPass PASS / $catFail FAIL" -ForegroundColor $color
}

Write-Host "`nFull results:" -ForegroundColor Gray
$results | Format-Table Category, Test, Status, Detail -AutoSize

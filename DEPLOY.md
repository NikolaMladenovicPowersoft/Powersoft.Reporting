# Powersoft Reporting — Deploy Procedure

## Prerequisites

- .NET 6 SDK installed
- FTP access to production server
- Solution builds without errors

## Production Server

| Field    | Value                          |
|----------|--------------------------------|
| FTP Host | `64.59.221.100`                |
| FTP Port | `21`                           |
| FTP User | `xOutsource`                   |
| FTP Pass | `6ww7M72m3s&g@`                |
| Site URL | `https://reports-ai.powersoft365.com` |
| SQL Server (CYTA) | `87.228.196.94`      |

## Step-by-step

### 1. Kill running local instances

```powershell
Get-Process -Name "Powersoft.Reporting.Web" -ErrorAction SilentlyContinue | Stop-Process -Force
```

### 2. Build & Publish

```powershell
cd C:\p\Powersoft.Reporting\Powersoft.Reporting.Web
dotnet publish -c Release -o C:\p\Powersoft.Reporting\publish --no-self-contained
```

Verify output: `Powersoft.Reporting.Web -> C:\p\Powersoft.Reporting\publish\`

### 3. Take site offline

Upload `app_offline.htm` to FTP root — IIS will serve this page while we upload files.

```powershell
curl.exe -s -T "C:\p\Powersoft.Reporting\publish\app_offline.htm" "ftp://64.59.221.100/" --user "xOutsource:6ww7M72m3s&g@"
```

If `app_offline.htm` doesn't exist in publish folder, create it first:

```powershell
@"
<!DOCTYPE html>
<html>
<head><title>Updating...</title></head>
<body><h1>Application is being updated. Please wait...</h1></body>
</html>
"@ | Set-Content "C:\p\Powersoft.Reporting\publish\app_offline.htm"
```

### 4. Upload all files via FTP

This script uploads everything **except** `appsettings.json` and `appsettings.Development.json` (production config must not be overwritten).

```powershell
$publishDir = "C:\p\Powersoft.Reporting\publish"
$ftpBase = "ftp://64.59.221.100/"
$cred = "xOutsource:6ww7M72m3s&g@"
$skip = @("appsettings.json", "appsettings.Development.json", "app_offline.htm")

$files = Get-ChildItem $publishDir -Recurse -File
$total = $files.Count; $i = 0; $errors = @()

foreach ($f in $files) {
    $rel = $f.FullName.Substring($publishDir.Length + 1).Replace("\", "/")
    if ($skip -contains $f.Name) { $i++; continue }
    $result = curl.exe -s -T $f.FullName --ftp-create-dirs ($ftpBase + $rel) --user $cred 2>&1
    $i++
    if ($LASTEXITCODE -ne 0) { $errors += "$rel : $result" }
    if ($i % 30 -eq 0) { Write-Host "$i / $total..." }
}

Write-Host "Done: $i / $total. Errors: $($errors.Count)"
if ($errors.Count -gt 0) { $errors | ForEach-Object { Write-Host "  ERR: $_" } }
```

Expected: `Done: ~168 / 168. Errors: 0`

### 5. Bring site back online

Delete `app_offline.htm` from FTP — IIS will restart the app automatically.

```powershell
curl.exe -s -Q "DELE app_offline.htm" "ftp://64.59.221.100/" --user "xOutsource:6ww7M72m3s&g@" 2>&1 | Select-Object -First 1
```

### 6. Verify

Open `https://reports-ai.powersoft365.com` in browser and confirm:
- Login page loads
- Can connect to a database
- Reports generate correctly

## Important Notes

### Files that must NEVER be overwritten on production

- `appsettings.json` — contains production connection string (encrypted password), API keys
- `appsettings.Development.json` — should not exist on production

### Database schema (dboReportsAI)

The application **automatically** creates the `dboReportsAI` schema and all Report-AI tables when:
- A user connects to a database (HomeController.Connect)
- The scheduler processes a database (ScheduleExecutionService)

No manual SQL scripts need to be run. The migration is idempotent.

### Typical deploy time

| Step | Duration |
|------|----------|
| Publish | ~10 sec |
| FTP upload (168 files) | ~80 sec |
| Total downtime | ~2 min |

### Rollback

If something goes wrong after deploy:
1. Upload `app_offline.htm` again (step 3)
2. Restore previous DLLs from a backup or rebuild from the last known good commit
3. Upload restored files (step 4)
4. Remove `app_offline.htm` (step 5)

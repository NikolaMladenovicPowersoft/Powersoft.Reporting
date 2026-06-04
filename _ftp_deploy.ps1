param(
    [string]$PublishDir = "C:\p\Powersoft.Reporting\publish",
    [string]$FtpBase    = "ftp://64.59.221.100/",
    [string]$FtpUser    = "xOutsource",
    [string]$FtpPass    = ""   # pass via -FtpPass on command line
)

$ErrorActionPreference = "Stop"
$skipFiles = @("appsettings.json", "appsettings.Development.json")

function Ftp-Upload($localPath, $remotePath) {
    $uri = $FtpBase.TrimEnd('/') + "/" + $remotePath.TrimStart('/')
    $req = [System.Net.FtpWebRequest]::Create($uri)
    $req.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $req.Credentials = [System.Net.NetworkCredential]::new($FtpUser, $FtpPass)
    $req.UseBinary = $true
    $req.UsePassive = $true
    $req.KeepAlive = $false
    $bytes = [System.IO.File]::ReadAllBytes($localPath)
    $req.ContentLength = $bytes.Length
    $stream = $req.GetRequestStream()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Close()
    $resp = $req.GetResponse()
    $resp.Close()
}

function Ftp-MkDir($remotePath) {
    try {
        $uri = $FtpBase.TrimEnd('/') + "/" + $remotePath.TrimStart('/')
        $req = [System.Net.FtpWebRequest]::Create($uri)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
        $req.Credentials = [System.Net.NetworkCredential]::new($FtpUser, $FtpPass)
        $req.UsePassive = $true
        $req.KeepAlive = $false
        $resp = $req.GetResponse()
        $resp.Close()
    } catch { <# dir may already exist #> }
}

function Ftp-Delete($remotePath) {
    try {
        $uri = $FtpBase.TrimEnd('/') + "/" + $remotePath.TrimStart('/')
        $req = [System.Net.FtpWebRequest]::Create($uri)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
        $req.Credentials = [System.Net.NetworkCredential]::new($FtpUser, $FtpPass)
        $req.UsePassive = $true
        $req.KeepAlive = $false
        $resp = $req.GetResponse()
        $resp.Close()
        Write-Host "Deleted $remotePath"
    } catch { Write-Host "Could not delete $remotePath (may not exist)" }
}

if (-not $FtpPass) {
    $FtpPass = Read-Host "FTP password for $FtpUser"
}

Write-Host "=== Step 1: Upload app_offline.htm ==="
Ftp-Upload "$PublishDir\app_offline.htm" "app_offline.htm"
Write-Host "Site is now offline."

Write-Host "=== Step 2: Upload all files ==="
$allFiles = Get-ChildItem $PublishDir -Recurse -File
$total = $allFiles.Count
$i = 0
foreach ($file in $allFiles) {
    $rel = $file.FullName.Substring($PublishDir.Length).TrimStart('\').Replace('\','/')
    $name = $file.Name

    # skip sensitive config and the offline marker itself
    if ($skipFiles -contains $name -or $rel -eq "app_offline.htm") {
        Write-Host "  SKIP $rel"
        continue
    }

    # ensure remote directory exists
    $dir = ($rel -replace '/[^/]+$','')
    if ($dir -and $dir -ne $rel) { Ftp-MkDir $dir }

    Write-Host "  [$i/$total] $rel"
    Ftp-Upload $file.FullName $rel
    $i++
}

Write-Host "=== Step 3: Remove app_offline.htm ==="
Ftp-Delete "app_offline.htm"
Write-Host "Site is back online."

# Builds "Sweety Void Setup.exe":
#   1. publishes the app as a self-contained single-file exe
#   2. packs the publish output into payload.zip
#   3. builds the Avalonia installer with the payload embedded
#
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
#         powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -SkipApp

param(
    [string]$Configuration = "Release",
    [switch]$SkipApp
)

$ErrorActionPreference = "Stop"

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$temp   = Join-Path $env:LOCALAPPDATA "Temp\opencode"
$appPub = Join-Path $temp "voidui-publish"
$payload = Join-Path $temp "payload.zip"
$insOut = Join-Path $temp "voidui-setup-out"

Write-Host "== Sweety Void installer build ==" -ForegroundColor Cyan

Get-Process -Name "VoidUI", "VoidUI.Setup" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (-not $SkipApp) {
    Write-Host "[1/3] Publishing app (self-contained single-file)..." -ForegroundColor Yellow
    if (Test-Path $appPub) { Remove-Item $appPub -Recurse -Force }
    # The app's obj is relocated to C: for this build (see -p:BaseIntermediateOutputPath).
    # Remove the stale obj at the default location, otherwise its generated *.g.cs files
    # get picked up by the default **\*.cs compile glob -> duplicate-class errors.
    $staleObj = Join-Path $root "obj"
    if (Test-Path $staleObj) { Remove-Item $staleObj -Recurse -Force -ErrorAction SilentlyContinue }
    $tempObj = Join-Path $temp "voidui-app-obj"
    if (Test-Path $tempObj) { Remove-Item $tempObj -Recurse -Force -ErrorAction SilentlyContinue }
    # Leftover RID build output from previous attempts can fill D:.
    $staleRid = Join-Path $root "_build\bin\net8.0-windows\win-x64"
    if (Test-Path $staleRid) { Remove-Item $staleRid -Recurse -Force -ErrorAction SilentlyContinue }
    $tempBin = Join-Path $temp "voidui-app-bin"
    if (Test-Path $tempBin) { Remove-Item $tempBin -Recurse -Force -ErrorAction SilentlyContinue }
    dotnet publish (Join-Path $root "VoidUI.csproj") `
        -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:BaseIntermediateOutputPath="$temp\voidui-app-obj\" `
        -p:OutputPath="$temp\voidui-app-bin\" `
        -o $appPub
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (app) failed" }
    # The build disk is nearly full: drop intermediates right after publish.
    foreach ($d in @($tempObj, $tempBin)) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue }
    }

    Write-Host "[2/3] Packing payload.zip..." -ForegroundColor Yellow
    if (Test-Path $payload) { Remove-Item $payload -Force }
    Compress-Archive -Path (Join-Path $appPub "*") -DestinationPath $payload -CompressionLevel Optimal
    # Payload is packed; the publish dir is no longer needed either.
    if (Test-Path $appPub) { Remove-Item $appPub -Recurse -Force -ErrorAction SilentlyContinue }
}
elseif (-not (Test-Path $payload)) {
    throw "payload.zip not found at $payload - run without -SkipApp first"
}

Write-Host "[3/3] Building installer..." -ForegroundColor Yellow
if (Test-Path $insOut) { Remove-Item $insOut -Recurse -Force }
dotnet publish (Join-Path $root "Installer\VoidUI.Installer.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PayloadZip="$payload" `
    -o $insOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (installer) failed" }

$setup = Join-Path $insOut "VoidUI.Setup.exe"
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host ""
Write-Host "DONE: $setup ($sizeMb MB)" -ForegroundColor Green

# Try to drop a copy next to the app for convenience.
$deliver = Join-Path $root "_build\installer"
try {
    New-Item -ItemType Directory -Path $deliver -Force | Out-Null
    Copy-Item $setup $deliver -Force
    Write-Host "Copied to: $deliver\VoidUI.Setup.exe" -ForegroundColor Green
}
catch {
    Write-Host "Could not copy to $deliver (disk space?) - installer stays at $setup" -ForegroundColor DarkYellow
}

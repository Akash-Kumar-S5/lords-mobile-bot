param(
    [string]$ExtractRoot = "D:\VS\Extract\WindowsBuild",
    [switch]$PersistEnvVar
)

$ErrorActionPreference = "Stop"

$payloadVsix = "C:\ProgramData\Microsoft\VisualStudio\Packages\Microsoft.VisualStudio.Windows.Build,version=17.14.36518.7,productarch=neutral\payload.vsix"
$appxDir = Join-Path $ExtractRoot "Contents\MSBuild\Microsoft\VisualStudio\v17.0\AppxPackage"
$appxTaskDll = Join-Path $appxDir "Microsoft.Build.AppxPackage.dll"
$priTaskDll = Join-Path $appxDir "Microsoft.Build.Packaging.Pri.Tasks.dll"

if (-not (Test-Path $payloadVsix)) {
    throw "Required VSIX payload not found: $payloadVsix"
}

if (-not (Test-Path $appxTaskDll) -or -not (Test-Path $priTaskDll)) {
    if (Test-Path $ExtractRoot) {
        Remove-Item $ExtractRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $ExtractRoot | Out-Null
    tar -xf $payloadVsix -C $ExtractRoot
}

if (-not (Test-Path $appxTaskDll) -or -not (Test-Path $priTaskDll)) {
    throw "Extraction failed. Appx task assemblies were not found under: $appxDir"
}

Write-Host "WinUI Appx tool path ready:" -ForegroundColor Green
Write-Host "  $appxDir"

if ($PersistEnvVar) {
    [Environment]::SetEnvironmentVariable("WINUI_APPX_TOOLS_PATH", $appxDir, "User")
    Write-Host "Set user environment variable WINUI_APPX_TOOLS_PATH." -ForegroundColor Yellow
    Write-Host "Restart terminal/IDE before building." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Build command:" -ForegroundColor Cyan
Write-Host "  dotnet build LordsBot.sln -c Release"

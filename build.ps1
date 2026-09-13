$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\Leigod_Auto_Pause\Leigod_Auto_Pause.csproj"
$Output = Join-Path $Root "publish"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK 未安装或 dotnet 不在 PATH 中。请先安装 .NET 8 SDK。"
}

Write-Host "[1/3] Cleaning publish directory..."
if (Test-Path $Output) {
    Remove-Item $Output -Recurse -Force
}

Write-Host "[2/3] Restoring dependencies..."
dotnet restore $Project
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "[3/3] Publishing self-contained win-x64 single-file launcher..."
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $Output `
    --no-restore

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$Exe = Join-Path $Output "Leigod_Auto_Pause.exe"
if (-not (Test-Path $Exe)) {
    throw "编译命令成功结束，但未找到 $Exe"
}

$Hash = Get-FileHash $Exe -Algorithm SHA256
Write-Host ""
Write-Host "Build completed."
Write-Host "Output : $Exe"
Write-Host "SHA256 : $($Hash.Hash.ToLowerInvariant())"

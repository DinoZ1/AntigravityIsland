# Antigravity Dynamic Island - Build Script PowerShell
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$framework\WPF"

$refs = @(
    "/r:`"$wpf\PresentationFramework.dll`"",
    "/r:`"$wpf\PresentationCore.dll`"",
    "/r:`"$wpf\WindowsBase.dll`"",
    "/r:`"$wpf\System.Speech.dll`"",
    "/r:`"$framework\System.Xaml.dll`"",
    "/r:`"$framework\System.Drawing.dll`"",
    "/r:`"$framework\System.Web.Extensions.dll`""
)

$srcDir = Join-Path $PSScriptRoot "src"
$sources = Get-ChildItem -Path "$srcDir\*.cs" | ForEach-Object { $_.FullName }
$outFile = Join-Path $PSScriptRoot "AntigravityIsland.exe"

Stop-Process -Name "AntigravityIsland" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

Write-Host "Compilando Antigravity Dynamic Island..." -ForegroundColor Cyan

& $csc /nologo /target:winexe /optimize+ /out:$outFile $refs $sources

if ($LASTEXITCODE -eq 0) {
    $size = (Get-Item $outFile).Length
    Write-Host "[SUCESSO] Binario compilado: $outFile ($size bytes)" -ForegroundColor Green
} else {
    Write-Host "[ERRO] Falha na compilacao." -ForegroundColor Red
}

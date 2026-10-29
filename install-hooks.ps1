# Antigravity Dynamic Island - Instalador de Hooks para o Antigravity CLI
param(
    [switch]$Uninstall,
    [switch]$UsePowerShellBridge
)

$ErrorActionPreference = 'Stop'
$globalConfigDir = Join-Path $env:USERPROFILE ".gemini\config"
$hooksFile = Join-Path $globalConfigDir "hooks.json"
$hooksBackup = Join-Path $globalConfigDir "hooks.json.bak"

if (-not (Test-Path $globalConfigDir)) {
    New-Item -ItemType Directory -Path $globalConfigDir -Force | Out-Null
}

$hooksConfig = @{}
if (Test-Path $hooksFile) {
    try {
        $hooksConfig = Get-Content -Raw -Path $hooksFile -Encoding UTF8 | ConvertFrom-Json
        # Converte para hashtable mutavel se necessario
        $hooksConfig = [System.Collections.Hashtable]$hooksConfig
    } catch {
        Copy-Item -Path $hooksFile -Destination $hooksBackup -Force
        $hooksConfig = @{}
    }
}

if ($Uninstall) {
    if ($hooksConfig.ContainsKey("antigravity-dynamic-island")) {
        $hooksConfig.Remove("antigravity-dynamic-island")
        $json = ConvertTo-Json -InputObject $hooksConfig -Depth 10
        Set-Content -Path $hooksFile -Value $json -Encoding UTF8
        Write-Host "[SUCESSO] Hooks da Dynamic Island removidos do Antigravity CLI." -ForegroundColor Yellow
    } else {
        Write-Host "[INFO] Hooks da Dynamic Island nao estavam instalados." -ForegroundColor Gray
    }
    exit 0
}

# Caminho do bridge
$bridgeScript = if ($UsePowerShellBridge) {
    Join-Path $PSScriptRoot "hooks\agy-island-bridge.ps1"
} else {
    Join-Path $PSScriptRoot "hooks\agy-island-bridge.js"
}

$cmdPrefix = if ($UsePowerShellBridge) {
    "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$bridgeScript`""
} else {
    "node `"$bridgeScript`""
}

# Monta a especificacao oficial de hooks do Antigravity CLI
$islandHook = @{
    enabled = $true
    PreToolUse = @(
        @{
            matcher = "*"
            hooks = @(
                @{
                    type = "command"
                    command = "$cmdPrefix PreToolUse"
                    timeout = 305
                }
            )
        }
    )
    PreInvocation = @(
        @{
            type = "command"
            command = "$cmdPrefix PreInvocation"
            timeout = 5
        }
    )
    PostToolUse = @(
        @{
            matcher = "*"
            hooks = @(
                @{
                    type = "command"
                    command = "$cmdPrefix PostToolUse"
                    timeout = 5
                }
            )
        }
    )
    Stop = @(
        @{
            type = "command"
            command = "$cmdPrefix Stop"
            timeout = 10
        }
    )
}

$hooksConfig["antigravity-dynamic-island"] = $islandHook

if (Test-Path $hooksFile) {
    Copy-Item -Path $hooksFile -Destination $hooksBackup -Force
}

$jsonOut = ConvertTo-Json -InputObject $hooksConfig -Depth 10
Set-Content -Path $hooksFile -Value $jsonOut -Encoding UTF8

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "[SUCESSO] Hooks da Antigravity Dynamic Island instalados!" -ForegroundColor Green
Write-Host "Arquivo de configuracao: $hooksFile" -ForegroundColor Gray
Write-Host "Comando bridge: $cmdPrefix" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Agora inicie o 'AntigravityIsland.exe' e utilize o Antigravity CLI normalmente." -ForegroundColor White

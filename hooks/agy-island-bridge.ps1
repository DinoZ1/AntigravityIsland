# Antigravity Dynamic Island - PowerShell Hook Bridge
param([string]$EventType = 'PreToolUse')

$ErrorActionPreference = 'SilentlyContinue'
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$inputRaw = [Console]::In.ReadToEnd()
$payload = @{}
try {
    if ($inputRaw) { $payload = ConvertFrom-Json -InputObject $inputRaw }
} catch {}

$parentPid = 0
try {
    $parentPid = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
} catch {}

$bodyObj = @{
    agent = 'antigravity'
    eventType = $EventType
    conversationId = $payload.conversationId
    toolCall = $payload.toolCall
    stepIdx = $payload.stepIdx
    pid = $parentPid
}

$jsonBody = ConvertTo-Json -InputObject $bodyObj -Depth 6 -Compress

$timeout = if ($EventType -eq 'PreToolUse') { 300 } else { 4 }

try {
    $response = Invoke-RestMethod -Uri "http://127.0.0.1:47823/api/hook" -Method Post -Body $jsonBody -ContentType "application/json" -TimeoutSec $timeout
    if ($response.decision) {
        $result = @{ decision = $response.decision; reason = $response.reason }
    } else {
        $result = @{ decision = "allow" }
    }
} catch {
    if ($EventType -eq 'PreInvocation') {
        $result = @{ injectSteps = @() }
    } elseif ($EventType -eq 'PreToolUse') {
        $result = @{ decision = "allow" }
    } else {
        $result = @{}
    }
}

[Console]::Out.Write((ConvertTo-Json -InputObject $result -Compress))
exit 0

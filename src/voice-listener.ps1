# Voice Listener usando Windows.Media.SpeechRecognition
$ErrorActionPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

try {
    $null = [Windows.Media.SpeechRecognition.SpeechRecognizer, Windows.Media.SpeechRecognition, ContentType = WindowsRuntime]
    $recognizer = New-Object Windows.Media.SpeechRecognition.SpeechRecognizer
    $op = $recognizer.CompileConstraintsAsync()
    
    $timeout = 0
    while ($op.Status -eq [Windows.Foundation.AsyncStatus]::Started -and $timeout -lt 60) {
        Start-Sleep -Milliseconds 50
        $timeout++
    }

    if ($op.Status -ne [Windows.Foundation.AsyncStatus]::Completed) {
        Write-Output '{"ok":false,"error":"falha_compilacao"}'
        exit 0
    }

    $recOp = $recognizer.RecognizeAsync()
    $timeout = 0
    # Espera fala por ate 8 segundos
    while ($recOp.Status -eq [Windows.Foundation.AsyncStatus]::Started -and $timeout -lt 80) {
        Start-Sleep -Milliseconds 100
        $timeout++
    }

    if ($recOp.Status -eq [Windows.Foundation.AsyncStatus]::Completed) {
        $result = $recOp.GetResults()
        $text = $result.Text
        $confidence = $result.Confidence.ToString()
        Write-Output ("{`"ok`":true,`"text`":`"" + ($text -replace '["\\]', ' ') + "`",`"confidence`":`"$confidence`"}")
    } else {
        Write-Output '{"ok":false,"error":"sem_fala"}'
    }
} catch {
    Write-Output ("{`"ok`":false,`"error`":`"" + ($_.Exception.Message -replace '["\\]', ' ') + "`"}")
}

@echo off
setlocal
echo ========================================================
echo Compilando Antigravity Dynamic Island (Clean-Room Win32)
echo ========================================================

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF

if not exist "%CSC%" (
    echo [ERRO] Compilador csc.exe nao encontrado em %CSC%
    pause
    exit /b 1
)

:: Fecha instancia anterior se estiver em execucao para liberar o arquivo
taskkill /F /IM AntigravityIsland.exe >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

"%CSC%" /nologo /target:winexe /optimize+ /out:"AntigravityIsland.exe" ^
    /r:"%WPF%\PresentationFramework.dll" ^
    /r:"%WPF%\PresentationCore.dll" ^
    /r:"%WPF%\WindowsBase.dll" ^
    /r:"%WPF%\System.Speech.dll" ^
    /r:"%FW%\System.Xaml.dll" ^
    /r:"%FW%\System.Drawing.dll" ^
    /r:"%FW%\System.Web.Extensions.dll" ^
    src\*.cs

if %ERRORLEVEL% equ 0 (
    echo [SUCESSO] AntigravityIsland.exe gerado com sucesso!
    echo Tamanho do binario:
    for %%I in (AntigravityIsland.exe) do echo %%~zI bytes
) else (
    echo [FALHA] Erro na compilacao.
)

endlocal

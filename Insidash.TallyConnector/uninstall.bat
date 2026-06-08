@echo off
:: Stop and uninstall the Insidash Tally Connector Service
:: Requires Administrator privileges!

set EXE_PATH=%~dp0bin\Debug\net48\Insidash.TallyConnector.exe
if not exist "%EXE_PATH%" (
    set EXE_PATH=%~dp0bin\Release\net48\Insidash.TallyConnector.exe
)

echo Stopping service...
sc stop InsidashTallyConnector

echo Uninstalling service using InstallUtil.exe...
C:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe /u "%EXE_PATH%"

pause

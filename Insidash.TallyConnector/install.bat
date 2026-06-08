@echo off
:: Install and start the Insidash Tally Connector Service
:: Requires Administrator privileges!

set EXE_PATH=%~dp0bin\Debug\net48\Insidash.TallyConnector.exe
if not exist "%EXE_PATH%" (
    set EXE_PATH=%~dp0bin\Release\net48\Insidash.TallyConnector.exe
)

if not exist "%EXE_PATH%" (
    echo Error: Could not find compiled binary. Please build the project first.
    pause
    exit /b 1
)

echo Installing service using InstallUtil.exe...
C:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe "%EXE_PATH%"

echo Starting service...
sc start InsidashTallyConnector

echo Querying service status...
sc query InsidashTallyConnector

pause

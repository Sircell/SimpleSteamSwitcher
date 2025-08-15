@echo off
echo Closing any running instances...
taskkill /F /IM SimpleSteamSwitcher.exe 2>nul

echo Building with diagnostic logging...
dotnet build
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo Running application with cache debugging...
dotnet run

pause 
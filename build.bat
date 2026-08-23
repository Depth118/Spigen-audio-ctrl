@echo off
title Building Spigen Audio CTRL
echo ========================================================
echo   Building Spigen Audio CTRL Native Windows App (.NET 8)
echo ========================================================
echo.

set DOTNET_EXE="%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not exist %DOTNET_EXE% (
    set DOTNET_EXE=dotnet
)

%DOTNET_EXE% publish "SpigenAudioCTRL.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "dist\"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================================
    echo   BUILD SUCCESSFUL!
    echo   Standalone Executable: dist\SpigenAudioCTRL.exe
    echo ========================================================
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%
)

pause

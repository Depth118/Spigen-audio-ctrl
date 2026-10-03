@echo off
setlocal
title Building Spigen Audio CTRL

set DOTNET=dotnet
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set DOTNET="%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
set PUBLISH=%DOTNET% publish SpigenAudioCTRL.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:DebugType=none

echo Running tests...
%DOTNET% test tests\SpigenAudioCTRL.Tests\SpigenAudioCTRL.Tests.csproj -c Release || goto :failed

echo.
echo Publishing standalone build (no .NET installation needed)...
%PUBLISH% --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist || goto :failed

echo.
echo Publishing small build (needs the .NET 8 Desktop Runtime)...
%PUBLISH% --self-contained false -o dist\requires-dotnet8 || goto :failed

echo.
echo ========================================================
echo   BUILD SUCCESSFUL
echo   Standalone: dist\SpigenAudioCTRL.exe
echo   Small:      dist\requires-dotnet8\SpigenAudioCTRL.exe
echo ========================================================
pause
exit /b 0

:failed
echo.
echo Build failed with error code %ERRORLEVEL%
pause
exit /b 1

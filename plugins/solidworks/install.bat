@echo off
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Please run this script as Administrator.
    pause
    exit /b
)
set REGASM="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
%REGASM% /codebase "%~dp0SupportFins.SolidWorks\bin\x64\Debug\SupportFins.SolidWorks.dll"
echo Support Fins registered successfully!
pause

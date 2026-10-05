@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: .NET Framework 4.x C# compiler is unavailable.
  exit /b 1
)
if not exist out mkdir out
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 /out:out\BullyCoopLauncher.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll BullyCoopLauncher.cs
if errorlevel 1 exit /b 1
if not exist "out\BullyCoopLauncher.exe" (
  echo ERROR: Launcher was not created.
  exit /b 1
)
echo SUCCESS: out\BullyCoopLauncher.exe

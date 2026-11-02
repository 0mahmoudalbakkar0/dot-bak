@echo off
setlocal
rem Builds dist\BAK.exe with the C# compiler that ships with Windows (.NET Framework 4).
rem No SDK, no Visual Studio, no NuGet packages needed.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [BAK] .NET Framework 4 compiler not found. It ships with Windows 10 and 11.
  if /i not "%~1"=="nopause" if "%CI%"=="" pause
  exit /b 1
)

if not exist "%~dp0dist" mkdir "%~dp0dist"

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%~dp0dist\BAK.exe" ^
  /win32icon:"%~dp0assets\icon.ico" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  "%~dp0src\AssemblyInfo.cs" "%~dp0src\Core.cs" "%~dp0src\Strings.cs" "%~dp0src\Wheel.cs" "%~dp0src\Ui.cs" "%~dp0src\App.cs"
if errorlevel 1 (
  echo.
  echo [BAK] Build failed. Please open an issue and paste the messages above.
  if /i not "%~1"=="nopause" if "%CI%"=="" pause
  exit /b 1
)
echo [BAK] Built dist\BAK.exe
exit /b 0

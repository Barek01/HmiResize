@echo off
rem Builds HmiResize.exe and HmiResizeGui.exe with the .NET Framework 4.8 C# compiler (shipped with Windows).
rem Usage: build.bat [TIA Portal version, e.g. 21]   (default: 21)
setlocal
set TIAVER=%1
if "%TIAVER%"=="" set TIAVER=21

set PORTAL=%ProgramFiles%\Siemens\Automation\Portal V%TIAVER%
set API=%PORTAL%\PublicAPI\V%TIAVER%\net48
set BIN=%PORTAL%\Bin\PublicAPI
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%API%\Siemens.Engineering.Base.dll" (
  echo Openness assemblies not found in "%API%"
  exit /b 1
)

cd /d "%~dp0"
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /out:HmiResize.exe ^
  /reference:"%API%\Siemens.Engineering.Base.dll" ^
  /reference:"%API%\Siemens.Engineering.WinCCUnified.dll" ^
  /reference:"%BIN%\Siemens.Engineering.Contract.dll" ^
  /reference:"%BIN%\Siemens.Engineering.ClientAdapter.Interfaces.dll" ^
  src\HmiResize.cs
if errorlevel 1 exit /b 1
echo HmiResize.exe built for TIA Portal V%TIAVER%.

rem GUI (drives HmiResize.exe, no Openness dependency)
"%CSC%" /nologo /target:winexe /optimize+ /out:HmiResizeGui.exe ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  src\HmiResizeGui.cs
if errorlevel 1 exit /b 1
echo HmiResizeGui.exe built.

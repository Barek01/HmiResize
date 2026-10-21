@echo off
rem Compile HmiResize.exe avec le compilateur C# du .NET Framework 4.8 (fourni avec Windows).
rem Usage : build.bat [version TIA, ex. 21]   (defaut : 21)
setlocal
set TIAVER=%1
if "%TIAVER%"=="" set TIAVER=21

set PORTAL=%ProgramFiles%\Siemens\Automation\Portal V%TIAVER%
set API=%PORTAL%\PublicAPI\V%TIAVER%\net48
set BIN=%PORTAL%\Bin\PublicAPI
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%API%\Siemens.Engineering.Base.dll" (
  echo DLL Openness introuvables dans "%API%"
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
echo HmiResize.exe compile pour TIA Portal V%TIAVER%.

rem Interface graphique (pilote HmiResize.exe, pas de dependance Openness)
"%CSC%" /nologo /target:winexe /optimize+ /out:HmiResizeGui.exe ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  src\HmiResizeGui.cs
if errorlevel 1 exit /b 1
echo HmiResizeGui.exe compile.

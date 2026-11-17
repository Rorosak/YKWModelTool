@echo off
setlocal
cd /d "%~dp0"

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo ERROR: Could not locate .NET Framework v4.0 csc.exe.
    exit /b 1
)

echo Compiling YKWModelTool.exe using "%CSC%"...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
  /out:"%~dp0YKWModelTool.exe" ^
  /reference:"%~dp0Metanoia\Metanoia.exe" ^
  /reference:"%~dp0Metanoia\OpenTK.dll" ^
  /reference:"%~dp0Metanoia\OpenTK.GLControl.dll" ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Xml.dll ^
  /reference:System.Core.dll ^
  "%~dp0src\YKWModelTool.cs"

if errorlevel 1 (
    echo Build FAILED!
    exit /b %errorlevel%
)

echo Build succeeded: "%~dp0YKWModelTool.exe"
endlocal

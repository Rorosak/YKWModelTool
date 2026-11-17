@echo off
setlocal
if "%~1"=="" (
    start "" "%~dp0YKWModelTool.exe"
    exit /b 0
)

:loop
if "%~1"=="" goto done
echo Exporting "%~1"...
start "" /wait /b "%~dp0YKWModelTool.exe" --cli "%~1"
shift
goto loop

:done
echo.
pause

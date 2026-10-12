@echo off
setlocal
set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe
echo Building YiYiTranslator.exe ...
"%CSC%" @app.rsp
if errorlevel 1 goto fail
echo.
echo OK: YiYiTranslator.exe
exit /b 0
:fail
echo.
echo BUILD FAILED
exit /b 1

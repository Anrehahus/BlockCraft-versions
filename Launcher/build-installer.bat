@echo off
chcp 65001 >nul
cd /d "%~dp0"
call "%~dp0_node.bat"
if errorlevel 1 ( pause & exit /b 1 )
call npm install
if errorlevel 1 ( pause & exit /b 1 )
call npm run dist
if errorlevel 1 ( pause & exit /b 1 )
echo.
echo Готово! Установщик лежит в папке dist
start "" dist
pause

@echo off
chcp 65001 >nul
cd /d "%~dp0"
call "%~dp0_node.bat"
if errorlevel 1 ( pause & exit /b 1 )
if not exist node_modules\electron\dist\electron.exe (
  echo Первый запуск: устанавливаю зависимости, это займёт пару минут...
  call npm install
  if errorlevel 1 ( pause & exit /b 1 )
)
rem Запускаем лаунчер отдельным процессом и закрываем консоль: окно лаунчера от неё не зависит
start "" "node_modules\electron\dist\electron.exe" .
exit

@echo off
rem Подключает Node.js: системный, а если его нет - скачивает портативный в папку .node (один раз, ~30 МБ)
where node >nul 2>nul && exit /b 0
set "NODE_VER=20.18.1"
set "ARCH=x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "ARCH=arm64"
set "NODE_DIR=%~dp0.node\node-v%NODE_VER%-win-%ARCH%"
if not exist "%NODE_DIR%\node.exe" (
  echo Node.js не найден. Скачиваю портативную версию, это делается один раз...
  mkdir "%~dp0.node" 2>nul
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest 'https://nodejs.org/dist/v%NODE_VER%/node-v%NODE_VER%-win-%ARCH%.zip' -OutFile '%~dp0.node\node.zip'; Expand-Archive -Force '%~dp0.node\node.zip' '%~dp0.node'"
  del "%~dp0.node\node.zip" >nul 2>nul
)
if not exist "%NODE_DIR%\node.exe" (
  echo Не удалось скачать Node.js. Проверьте интернет или установите его вручную: https://nodejs.org
  exit /b 1
)
set "PATH=%NODE_DIR%;%PATH%"
exit /b 0

@echo off
set "PATH=C:\Program Files (x86)\Microsoft Visual Studio\Installer;%PATH%"
cd /d "%~dp0"
dotnet publish -c Release -r win-x64

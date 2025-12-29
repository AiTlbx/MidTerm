@echo off
echo #################################################
echo # Clean build artifacts
echo #################################################
echo.
taskkill /IM MSBuild.exe /F 2>nul
taskkill /IM VBCSCompiler.exe /F 2>nul
RD /S /Q .vs 2>nul
RD /S /Q TestResults 2>nul
del /S /F /AH *.suo 2>nul
del /S /F *.user 2>nul
del /S /F *.userprefs 2>nul
del /S /F *.bak 2>nul
FOR /D /R %%X IN (bin,obj) DO IF EXIST "%%X" (
    echo Deleting "%%X"
    RD /S /Q "%%X"
)
echo.
echo Clean complete.

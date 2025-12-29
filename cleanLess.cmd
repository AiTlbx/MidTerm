@echo off
echo #################################################
echo # Clean build artifacts (with restore)
echo #################################################
echo.
taskkill /IM MSBuild.exe /F 2>nul
taskkill /IM VBCSCompiler.exe /F 2>nul
RD /S /Q TestResults 2>nul
del /S /F *.userprefs 2>nul
del /S /F *.user 2>nul
del /S /F *.bak 2>nul

FOR /R %%X IN (bin,obj) DO (
    IF EXIST "%%X" (
        echo Deleting "%%X"
        RD /S /Q "%%X"
    )
)

echo.
echo Restoring packages...
dotnet restore
echo.
echo Clean complete.
pause

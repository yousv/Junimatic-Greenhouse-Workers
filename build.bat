@echo off
echo Project: JunimaticGreenhouseWorkers
echo Output: bin\Debug\net6.0\JunimaticGreenhouseWorkers.dll
echo Deploy: D:\Steam\steamapps\common\Stardew Valley\mods-testing\Junimatic Greenhouse Workers\
echo.
set /p choice="Enter 1 to build and deploy, 0 to exit: "
if "%choice%"=="1" (
    dotnet build "JunimaticGreenhouseWorkers.csproj" -v q -nologo
    if %errorlevel% neq 0 (
        echo Build failed.
    ) else (
        echo Build succeeded. Deployed to mods-testing.
    )
) else (
    if "%choice%"=="0" (
        echo Exiting.
    ) else (
        echo Invalid choice. Exiting.
    )
)
pause

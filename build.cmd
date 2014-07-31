@echo on
cd %~dp0

SETLOCAL
SET CACHED_NUGET=%LocalAppData%\NuGet\NuGet.exe

IF EXIST %CACHED_NUGET% goto copynuget
echo Downloading latest version of NuGet.exe...
IF NOT EXIST %LocalAppData%\NuGet md %LocalAppData%\NuGet
@powershell -NoProfile -ExecutionPolicy unrestricted -Command "$ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest 'https://www.nuget.org/nuget.exe' -OutFile '%CACHED_NUGET%'"

:copynuget
IF EXIST .nuget\nuget.exe goto restore
md .nuget
copy %CACHED_NUGET% .nuget\nuget.exe > nul

:restore
.nuget\NuGet.exe install FSharpSupport -ExcludeVersion -o packages -nocache -pre
.nuget\NuGet.exe install KoreBuild -ExcludeVersion -o packages -nocache -pre

IF "%SKIP_KRE_INSTALL%"=="1" goto run
CALL packages\KoreBuild\build\kvm upgrade -svr50 -x86
REM CALL packages\KoreBuild\build\kvm install default -svrc50 -x86

:run
for /f "usebackq tokens=*" %%a in (`where kpm`) do set KPM_PATH=%%a
for %%F in (%KPM_PATH%) do set KPM_DIR=%%~dpF

cd src\FSharpSupport

SET ERRORLEVEL=
echo klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0\packages\FSharpSupport\lib\net45" "Microsoft.Framework.PackageManager" build
call klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0\packages\FSharpSupport\lib\net45" "Microsoft.Framework.PackageManager" build
exit /b %ERRORLEVEL%

echo DONE
@echo off
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
.nuget\NuGet.exe install KoreBuild -ExcludeVersion -o packages -nocache -pre

IF "%SKIP_KRE_INSTALL%"=="1" goto run
CALL packages\KoreBuild\build\kvm upgrade -svr50 -x86
REM CALL packages\KoreBuild\build\kvm install default -svrc50 -x86

:run
REM Get the path of `kpm` and store it as KPM_PATH
for /f "usebackq tokens=*" %%a in (`where kpm`) do set KPM_PATH=%%a
REM Get the dir name of `KPM_PATH`
for %%F in (%KPM_PATH%) do set KPM_DIR=%%~dpF

cd "%~dp0src\FSharpSupport"
call kpm restore
REM @echo on

rd /s /q "%~dp0obj"
mkdir "%~dp0obj\pass0"
mkdir "%~dp0obj\pass1"
mkdir "%~dp0obj\pass2"
mkdir "%~dp0obj\pass3"

SET BOOTSTRAPPED=0
copy "%~dp0packages\FSharpSupport\lib\net45\FSharpSupport.dll" "%~dp0obj\pass1\FSharpSupport.dll" > nul

:build
SET ERRORLEVEL=
REM First build to make sure source is valid
call klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0obj\pass1" "Microsoft.Framework.PackageManager" build
REM @echo on
IF NOT "%ERRORLEVEL%" == "0" goto bootstrap

REM build again to make sure it fills the contracts
move /Y "%~dp0src\FSharpSupport\bin\debug\net45\FSharpSupport.dll" "%~dp0obj\pass2\FSharpSupport.dll" > nul
move /Y "%~dp0src\FSharpSupport\bin\debug\net45\FSharpSupport.pdb" "%~dp0obj\pass2\FSharpSupport.pdb" > nul
call klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0obj\pass2" "Microsoft.Framework.PackageManager" build
REM @echo on
IF NOT "%ERRORLEVEL%" == "0" goto end

REM build again to make sure it constructs something that fills the contracts
move /Y "%~dp0src\FSharpSupport\bin\debug\net45\FSharpSupport.dll" "%~dp0obj\pass3\FSharpSupport.dll" > nul
move /Y "%~dp0src\FSharpSupport\bin\debug\net45\FSharpSupport.pdb" "%~dp0obj\pass3\FSharpSupport.pdb" > nul
call klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0obj\pass3" "Microsoft.Framework.PackageManager" build
REM @echo on
IF NOT "%ERRORLEVEL%" == "0" goto end

IF "%K_BUILD_VERSION%" == "" goto noversion
IF NOT "%APPVEYOR_REPO_BRANCH%" == "master" goto wrongbranch
IF NOT "%APPVEYOR_PULL_REQUEST_NUMBER%" == "" goto pullreq
IF "%NUGET_SOURCE%" == "" goto end
echo Publishing "%~dp0src\FSharpSupport\bin\debug\FSharpSupport.0.1-alpha-%K_BUILD_VERSION%.nupkg"
%~dp0.nuget\NuGet.exe push "%~dp0src\FSharpSupport\bin\debug\FSharpSupport.0.1-alpha-%K_BUILD_VERSION%.nupkg" "%NUGET_API_KEY%" -Source "%NUGET_SOURCE%"

IF "%SYMBOL_SOURCE%" == "" goto end
echo Publishing "%~dp0src\FSharpSupport\bin\debug\FSharpSupport.0.1-alpha-%K_BUILD_VERSION%.symbols.nupkg"
%~dp0.nuget\NuGet.exe push "%~dp0src\FSharpSupport\bin\debug\FSharpSupport.0.1-alpha-%K_BUILD_VERSION%.symbols.nupkg" "%SYMBOL_API_KEY%" -Source "%SYMBOL_SOURCE%"
goto end

:wrongbranch
echo Skipping commit since branch = %APPVEYOR_REPO_NAME%
goto end

:pullreq
echo Skipping commit since it's a pull request
goto end

:noversion
echo Skipping commit since no version was provided
goto end

:bootstrap
IF NOT "%BOOTSTRAPPED%" == "0" goto end
set BOOTSTRAPPED="1"
echo Fowler prolly broke my code -.-
echo Bootstrapping
rd /s /q "%~dp0lib\Fowler"
mkdir "%~dp0lib"
git clone https://github.com/davidfowl/vNextLanguageSupport.git "%~dp0lib\Fowler"
cd "%~dp0lib\Fowler\src\FSharpSupport"
call kpm restore

SET ERRORLEVEL=
call kpm build
IF NOT "%ERRORLEVEL%" == "0" goto end
move /Y "%~dp0lib\Fowler\src\FSharpSupport\bin\debug\net45\FSharpSupport.dll" "%~dp0obj\pass0\FSharpSupport.dll" > nul
move /Y "%~dp0lib\Fowler\src\FSharpSupport\bin\debug\net45\FSharpSupport.pdb" "%~dp0obj\pass0\FSharpSupport.pdb" > nul
cd "%~dp0src\FSharpSupport"
call klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0obj\pass0" "Microsoft.Framework.PackageManager" build
REM @echo on
move /Y "%~dp0src\FSharpSupport\bin\debug\net45\FSharpSupport.dll" "%~dp0obj\pass1\FSharpSupport.dll" > nul
move /Y "%~dp0src\FSharpSupport\bin\debug\net45\FSharpSupport.pdb" "%~dp0obj\pass1\FSharpSupport.pdb" > nul
goto build

:end
exit /b %ERRORLEVEL%
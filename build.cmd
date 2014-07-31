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

cd src\FSharpSupport
call kpm restore

mkdir obj\pass1
mkdir obj\pass2
mkdir obj\pass3

SET ERRORLEVEL=
REM First build to make sure source is valid
move %~dp0\packages\FSharpSupport\lib\net45\FSharpSupport.dll obj\pass1\FSharpSupport.dll > nul
call klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0\src\FSharpSupport\obj\pass1" "Microsoft.Framework.PackageManager" build
IF NOT "%ERRORLEVEL%" == "0" goto end

REM build again to make sure it fills the contracts
move bin\debug\net45\FSharpSupport.dll obj\pass2\FSharpSupport.dll > nul
move bin\debug\net45\FSharpSupport.pdb obj\pass2\FSharpSupport.pdb > nul
call klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0\src\FSharpSupport\obj\pass2" "Microsoft.Framework.PackageManager" build
IF NOT "%ERRORLEVEL%" == "0" goto end

REM build again to make sure it constructs something that fills the contracts
move bin\debug\net45\FSharpSupport.dll obj\pass3\FSharpSupport.dll > nul
move bin\debug\net45\FSharpSupport.pdb obj\pass3\FSharpSupport.pdb > nul
call klr --lib "%KPM_DIR%;%KPM_DIR%\lib\Microsoft.Framework.PackageManager;%~dp0\src\FSharpSupport\obj\pass3" "Microsoft.Framework.PackageManager" build
IF NOT "%ERRORLEVEL%" == "0" goto end

IF "%K_BUILD_VERSION%" == "" goto end
IF NOT "%APPVEYOR_REPO_BRANCH%" == "master" goto wrongbranch
IF NOT "%APPVEYOR_PULL_REQUEST_NUMBER%" == "" goto pullreq
IF "%NUGET_SOURCE%" == "" goto end
echo Publishing "%~dp0\src\FSharpSupport\bin\debug\FSharpSupport.0.1-alpha-%K_BUILD_VERSION%.nupkg"
.nuget\NuGet.exe push "%~dp0\src\FSharpSupport\bin\debug\FSharpSupport.0.1-alpha-%K_BUILD_VERSION%.nupkg" "%NUGET_API_KEY%" -Source "%NUGET_SOURCE%"

IF "%SYMBOL_SOURCE%" == "" goto end
echo Publishing "%~dp0\src\FSharpSupport\bin\debug\FSharpSupport.0.1-alpha-%K_BUILD_VERSION%.symbols.nupkg"
.nuget\NuGet.exe push "%~dp0\src\FSharpSupport\bin\debug\FSharpSupport.0.1-alpha-%K_BUILD_VERSION%.symbols.nupkg" "%SYMBOL_API_KEY%" -Source "%SYMBOL_SOURCE%"
goto end

:wrongbranch
echo Skipping commit since branch = %APPVEYOR_REPO_NAME%
goto end

:pullreq
echo Skipping commit since it's a pull request
goto end

:end
exit /b %ERRORLEVEL%
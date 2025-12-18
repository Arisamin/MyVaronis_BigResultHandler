@echo off
setlocal enabledelayedexpansion

REM Script to add, commit, and push files to git
REM Usage: git-push.bat [file1] [file2] ... [fileN] "commit message"
REM 
REM Examples:
REM   git-push.bat docs\INTERVIEW_QUESTIONS.md "Add notification queue architecture questions"
REM   git-push.bat docs\ARCHITECTURE.md docs\INTERVIEW_QUESTIONS.md "Update architecture and interview docs"

REM Check if at least 2 arguments are provided (at least one file and a comment)
if "%~2"=="" (
    echo Usage: git-push.bat [file1] [file2] ... [fileN] "commit message"
    echo Example: git-push.bat docs\file1.md docs\file2.md "Updated documentation"
    exit /b 1
)

REM Collect all arguments except the last one (which is the commit message)
set "files="
set "lastArg="

:parseArgs
if "%~1"=="" goto :doneArgs
set "lastArg=%~1"
if not "%files%"=="" set "files=%files% "
if not "%~2"=="" (
    set "files=%files%%~1"
)
shift
goto :parseArgs

:doneArgs
REM lastArg now contains the commit message
set "commitMsg=%lastArg%"

REM Add files
echo Adding files: %files%
git add %files%
if errorlevel 1 (
    echo Error: git add failed
    exit /b 1
)

REM Commit
echo Committing with message: %commitMsg%
git commit -m "%commitMsg%"
if errorlevel 1 (
    echo Error: git commit failed
    exit /b 1
)

REM Push
echo Pushing to remote...
git push
if errorlevel 1 (
    echo Error: git push failed
    exit /b 1
)

echo Success: Changes pushed to remote repository
endlocal

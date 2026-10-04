param(
    [ValidateSet('win-x64', 'win-x86')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'vibrance.GUI\vibrance.GUI.csproj'
$outputDirectory = Join-Path $projectRoot "artifacts\$Runtime"
dotnet publish $projectFile -c Release -r $Runtime --self-contained true -o $outputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
$executable = Join-Path $outputDirectory 'vibrance.GUI.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Published executable is missing.' }
Get-FileHash -LiteralPath $executable -Algorithm SHA256

# Builds the program. Kept ASCII-only: Windows PowerShell 5.1 misreads UTF-8 scripts without a BOM.
#   .\publish.ps1                 one exe in dist, needs the .NET 10 Desktop Runtime
#   .\publish.ps1 -SelfContained  larger exe that also runs on a computer without .NET
#   .\publish.ps1 -Output <path>  builds into another folder
param(
    [switch]$SelfContained,
    [string]$Output = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\Marmelad\Marmelad.csproj'

# Compression shrinks the standalone exe from 206 MB to 82 MB. Unpacking to a temp folder on the
# first run matters too: decompressing in memory instead would add 100 MB to every launch.
$runtimeArguments = if ($SelfContained) {
    @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true', '-p:IncludeAllContentForSelfExtract=true')
} else {
    @('--self-contained', 'false')
}

dotnet publish $project -c Release -r win-x64 -o $Output @runtimeArguments `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none

if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
Write-Host "Done: $Output"

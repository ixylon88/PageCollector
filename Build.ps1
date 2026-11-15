<#
.SYNOPSIS
Builds the complete solution and optionally publishes the Windows x64 app.
.DESCRIPTION
Publishing is framework-dependent by default. Add -SelfContained to include
the .NET runtime. Each variant has its own output folder.
.EXAMPLE
.\Build.ps1
.EXAMPLE
.\Build.ps1 -Configuration Release -Publish
.EXAMPLE
.\Build.ps1 -Publish -SelfContained
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Publish,
    [switch] $SelfContained,
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'

if ($SelfContained -and -not $Publish) {
    throw '-SelfContained requires -Publish.'
}

. (Join-Path $PSScriptRoot 'scripts/DotNet.ps1')
Push-Location $PSScriptRoot

try {
    $buildArguments = @('build', 'PageCollector.sln', '-c', $Configuration, '-m:1', '--nologo')

    if ($NoRestore) {
        $buildArguments += '--no-restore'
    }

    Invoke-DotNet -Arguments $buildArguments

    if ($Publish) {
        $mode = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
        $outputName = if ($SelfContained) { 'win-x64_self-contained' } else { 'win-x64' }
        $outputDirectory = Join-Path $PSScriptRoot "dist/$outputName"
        Write-Host "Publishing $mode to $outputDirectory"

        # Remove stale runtime files from this distribution only; preserve the other variant.
        if (Test-Path -LiteralPath $outputDirectory) {
            Remove-Item -LiteralPath $outputDirectory -Recurse -Force
        }

        $publishArguments = @(
            'publish', 'src/PageCollector.App/PageCollector.App.csproj',
            '-c', $Configuration, '-r', 'win-x64',
            '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(),
            '-o', $outputDirectory, '-m:1', '--nologo', '-p:PublishTrimmed=false'
        )

        if ($SelfContained) {
            $publishArguments += '-p:TargetLatestRuntimePatch=true'
        }

        # Publish needs its own restore/build for the selected RID and deployment mode.
        Invoke-DotNet -Arguments $publishArguments
    }
}
finally {
    Pop-Location
}

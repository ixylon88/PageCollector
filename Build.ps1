<#
.SYNOPSIS
Builds the complete solution and optionally publishes the Windows x64 app.
.EXAMPLE
.\Build.ps1
.EXAMPLE
.\Build.ps1 -Configuration Release -Publish
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Publish,
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/DotNet.ps1')
Push-Location $PSScriptRoot

try {
    $buildArguments = @('build', 'PageCollector.sln', '-c', $Configuration, '-m:1', '--nologo')

    if ($NoRestore) {
        $buildArguments += '--no-restore'
    }

    Invoke-DotNet -Arguments $buildArguments

    if ($Publish) {
        Invoke-DotNet -Arguments @(
            'publish', 'src/PageCollector.App/PageCollector.App.csproj',
            '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true',
            '-o', 'dist/win-x64', '-m:1', '--nologo'
        )
    }
}
finally {
    Pop-Location
}

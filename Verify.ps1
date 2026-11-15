<#
.SYNOPSIS
Runs the same restore, formatting, build and MSTest checks as GitHub Actions.
.EXAMPLE
.\Verify.ps1
.EXAMPLE
.\Verify.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/DotNet.ps1')
Push-Location $PSScriptRoot

try {
    Invoke-DotNet -Arguments @('restore', 'PageCollector.sln', '--disable-parallel', '--nologo')
    Invoke-DotNet -Arguments @(
        'format', 'PageCollector.sln', '--verify-no-changes', '--no-restore', '--severity', 'warn'
    )
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration -NoRestore

    $testTarget = 'PageCollector.sln'

    if ($env:OS -ne 'Windows_NT') {
        $testTarget = 'tests/PageCollector.Core.Tests/PageCollector.Core.Tests.csproj'
        Write-Host 'WPF tests require Windows; running the core tests on this platform.'
    }

    Invoke-DotNet -Arguments @(
        'test', $testTarget, '-c', $Configuration, '-m:1', '--no-build', '--no-restore',
        '--logger', 'trx', '--results-directory', 'artifacts/TestResults'
    )
}
finally {
    Pop-Location
}

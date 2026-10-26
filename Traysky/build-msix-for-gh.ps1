[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$RepoRoot,
    [string]$Project,
    [string]$Notes,
    [switch]$Prerelease,
    [switch]$SkipRelease,
    [switch]$AllowDirty
)

& (Join-Path $PSScriptRoot '..\build-msix-for-gh.ps1') @PSBoundParameters

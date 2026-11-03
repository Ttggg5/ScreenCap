<#
.SYNOPSIS
    Prepares a release: sets the version in ScreenCap.csproj, commits "Release <version>" and tags v<version>.
    Pushing the tag (the command is printed at the end) makes GitHub Actions build and publish it.

.EXAMPLE
    ./scripts/release.ps1 1.3.0
    ./scripts/release.ps1 1.3.0-beta.1   # a prerelease: published, but not offered by the self-update
#>
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)

$tag = "v$Version"
$number = $Version -replace '-.*$', '' # the exe's version has no prerelease suffix
$project = 'src/ScreenCap/ScreenCap.csproj'

$notes = ".github/release-notes/$tag.md"
# The release notes may be new or edited: they are committed with the release.
if (git status --porcelain --untracked-files=all | Where-Object { $_.Substring(3) -ne $notes }) { throw 'There are uncommitted changes; commit or stash them first.' }
if ((git branch --show-current) -ne 'main') { throw 'Releases are made from main.' }
if (git tag --list $tag) { throw "Tag $tag already exists." }

$text = Get-Content $project -Raw
if ($text -notmatch '<Version>([^<]*)</Version>') { throw "No <Version> in $project." }
$current = $Matches[1]
# The same number again is fine (1.3.0 after 1.3.0-beta.1); the same tag again was refused above.
if ([version]$number -lt [version]$current) { throw "Version $Version is older than $current." }
if ($number -ne $current) {
    # Keep the file's line endings and encoding: replace the one value only.
    $text = $text -replace '<Version>[^<]*</Version>', "<Version>$number</Version>"
    [IO.File]::WriteAllText((Resolve-Path $project), $text, [Text.UTF8Encoding]::new($false))
    git add $project
}

if (Test-Path $notes) { git add $notes }

git commit --allow-empty -m "Release $Version"
git tag $tag

Write-Host ''
Write-Host "Tagged $tag." -ForegroundColor Green
if (Test-Path $notes) {
    Write-Host "Release notes: $notes. The release is published as soon as it is built."
} else {
    Write-Host "No ${notes}: the release will be a draft with GitHub's generated notes; publish it on GitHub when ready."
}
Write-Host 'To build and publish it, push the commit and the tag:'
Write-Host "    git push origin main $tag"

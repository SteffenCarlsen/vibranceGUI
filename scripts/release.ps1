param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')]
    [string]$Commit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Za-z][0-9A-Za-z.+-]*$')]
    [string]$Version,
    [Parameter(Mandatory)][string]$ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$head = (& git -C $projectRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $Commit) { throw 'Checkout does not match the release commit.' }

$artifactPath = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$executable = Join-Path $artifactPath 'vibrance.GUI.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Verified executable is missing.' }
$expectedVersion = if ($Version.Contains('+')) { "$Version.$Commit" } else { "$Version+$Commit" }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion -ne $expectedVersion) {
    throw 'The downloaded executable version does not match the verified source commit.'
}

$tag = if ($Version.Contains('+')) { "v$Version.g$Commit" } else { "v$Version+g$Commit" }
& git check-ref-format "refs/tags/$tag"
if ($LASTEXITCODE -ne 0) { throw 'Invalid release tag.' }
# Validate existing tags, including annotated tags, before resuming a release.
$remoteRefs = @(& git -C $projectRoot ls-remote --tags origin "refs/tags/$tag" "refs/tags/$tag^{}")
if ($LASTEXITCODE -ne 0) { throw 'Could not check the existing release tag.' }
if ($remoteRefs.Count -gt 0) {
    $target = ($remoteRefs | Where-Object { $_.EndsWith("refs/tags/$tag^{}") } | Select-Object -First 1)
    if (-not $target) { $target = $remoteRefs[0] }
    if (($target -split '\s+')[0] -ne $Commit) { throw 'The existing release tag points to a different commit.' }
}

$releaseJson = & gh release view $tag --repo $Repository --json isDraft,url,assets
$release = if ($LASTEXITCODE -eq 0) { $releaseJson | ConvertFrom-Json } else { $null }
if ($release -and -not $release.isDraft) {
    if ($remoteRefs.Count -eq 0 -or @($release.assets | Where-Object {
        $_.name -in @('vibrance.GUI.exe', 'SHA256SUMS.txt') -and $_.size -gt 0
    }).Count -ne 2) { throw 'The published release is incomplete or has no verifiable source tag.' }
    $masterHead = & gh api "repos/$Repository/git/ref/heads/master" --jq '.object.sha'
    if ($LASTEXITCODE -ne 0) { throw 'Could not check the current master commit.' }
    if ($masterHead -eq $Commit) {
        & gh release edit $tag --repo $Repository --latest=true
        if ($LASTEXITCODE -ne 0) { throw 'Could not mark the completed master release Latest.' }
    }
    Write-Output "Release already published: $($release.url)"
    return # Published assets can be immutable; reruns only reconcile Latest.
}

# Include changes since the nearest completed ancestor release, even when an
# earlier push failed checks or another commit finished publishing out of order.
$previous = '919a9f2'
$distance = [int]::MaxValue
$releasedTags = @(& gh api --paginate "repos/$Repository/releases?per_page=100" --jq '.[] | select(.draft == false) | .tag_name')
if ($LASTEXITCODE -ne 0) { throw 'Could not read previously published releases.' }
foreach ($releasedTag in $releasedTags) {
    if ($releasedTag -notmatch '(?:\+|\.)g(?<source>[0-9a-f]{40})$') { continue }
    $candidate = $Matches.source
    & git -C $projectRoot merge-base --is-ancestor $candidate $Commit
    if ($LASTEXITCODE -ne 0) { continue } # Other branches or newer commits are not a baseline.
    $count = & git -C $projectRoot rev-list --count "$candidate..$Commit"
    if ($LASTEXITCODE -ne 0) { throw 'Could not compare release commits.' }
    if ([int]$count -gt 0 -and [int]$count -lt $distance) { $previous = $candidate; $distance = [int]$count }
}
$changes = & git -C $projectRoot log --reverse --format="## %s%n%n%b%nSource: https://github.com/$Repository/commit/%H%n" "$previous..$Commit"
if ($LASTEXITCODE -ne 0) { throw 'Could not generate release notes.' }
$notes = @"
# vibranceGUI $Version

Automatic Windows x64 release of the independently maintained fork.
Download vibrance.GUI.exe; the .NET 10 runtime is included. SHA256SUMS.txt
contains the executable checksum.

Source commit: https://github.com/$Repository/commit/$Commit
Compatibility and full fork changelog: https://github.com/$Repository/blob/$Commit/README.md

This fork release is not the original project's release. GPU/game/HDR compatibility
limits remain as documented in the README.

$($changes -join "`n")
"@
$notesFile = Join-Path $artifactPath 'release-notes.md'
Set-Content -LiteralPath $notesFile -Value $notes -Encoding utf8
$checksumFile = Join-Path $artifactPath 'SHA256SUMS.txt'
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumFile -Value "$hash  vibrance.GUI.exe" -Encoding ascii

if (-not $release) {
    & gh release create $tag --repo $Repository --target $Commit --draft --prerelease=false --latest=false `
        --title "vibranceGUI $Version ($($Commit.Substring(0, 12)))" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the draft release.' }
}
& gh release upload $tag $executable $checksumFile --repo $Repository --clobber
if ($LASTEXITCODE -ne 0) { throw 'Release assets were not uploaded; the release remains a draft.' }
$masterHead = & gh api "repos/$Repository/git/ref/heads/master" --jq '.object.sha'
if ($LASTEXITCODE -ne 0) { throw 'Could not check the current master commit; the release remains a draft.' }
$latest = if ($masterHead -eq $Commit) { 'true' } else { 'false' }
& gh release edit $tag --repo $Repository --target $Commit --notes-file $notesFile --draft=false --prerelease=false "--latest=$latest"
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the completed release.' }
Write-Output "Published release $tag from $Commit."

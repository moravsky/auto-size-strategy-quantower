param(
    [string]$Version = "2.0.1",
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Build-Release {
    param(
        [string]$RepoRoot,
        [string]$Version
    )

    $binDir = Join-Path $RepoRoot "AutoSizeStrategy\bin\Release"
    $artifactsDir = Join-Path $RepoRoot "artifacts"

    # 1. Build Release
    Write-Host "`n--- Building Release ($RepoRoot) ---"
    dotnet build "$RepoRoot\AutoSizeStrategy\AutoSizeStrategy.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }

    # 2. Ensure artifacts directory
    if (-not (Test-Path $artifactsDir)) {
        New-Item -ItemType Directory -Path $artifactsDir | Out-Null
    }

    # 3. Build installer
    Write-Host "`n--- Building Installer ---"
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if ($iscc) {
        iscc /DMyAppVersion=$Version "$RepoRoot\installer\setup.iss"
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    } else {
        Write-Warning "iscc not found -- skipping installer. Install via: winget install JRSoftware.InnoSetup"
    }

    # 4. Create ZIP
    Write-Host "`n--- Creating ZIP ---"
    $zipName = "AutoSizeStrategy-$Version.zip"
    $zipPath = Join-Path $artifactsDir $zipName

    $tempDir = Join-Path $env:TEMP "AutoSizeStrategy-zip-staging-$Version"
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tempDir | Out-Null

    Get-ChildItem $binDir -Filter "*.dll" | Copy-Item -Destination $tempDir
    Get-ChildItem $binDir -Filter "*.deps.json" | Copy-Item -Destination $tempDir
    Get-ChildItem $binDir -Filter "*.pdb" | Copy-Item -Destination $tempDir
    Copy-Item (Join-Path $RepoRoot "LICENSE") -Destination $tempDir

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath
    Remove-Item $tempDir -Recurse -Force

    Write-Host "`n--- Release artifacts in $artifactsDir ---"
    Get-ChildItem $artifactsDir | ForEach-Object { Write-Host "  $($_.Name)  ($([math]::Round($_.Length / 1KB)) KB)" }

    return $artifactsDir
}

# When publishing, build from the tag in an isolated worktree so the
# artifacts always match the tagged commit, never whatever is in the
# working tree. We call Build-Release directly with -RepoRoot pointed
# at the worktree -- no re-invocation needed.
if ($Publish) {
    $tag = "v$Version"
    git rev-parse --verify --quiet "refs/tags/$tag" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Tag $tag does not exist locally. Create and push it before publishing." }

    $worktreePath = Join-Path ([System.IO.Path]::GetTempPath()) "AutoSizeStrategy-release-$Version"
    if (Test-Path $worktreePath) {
        Write-Host "Removing stale worktree at $worktreePath"
        git worktree remove --force $worktreePath 2>$null
        if (Test-Path $worktreePath) { Remove-Item $worktreePath -Recurse -Force }
    }

    Write-Host "`n--- Building $tag from temporary worktree at $worktreePath ---"
    git worktree add --detach $worktreePath $tag
    if ($LASTEXITCODE -ne 0) { throw "git worktree add failed" }

    try {
        $worktreeArtifacts = Build-Release -RepoRoot $worktreePath -Version $Version

        $zipPath = Join-Path $worktreeArtifacts "AutoSizeStrategy-$Version.zip"
        $setupExe = Join-Path $worktreeArtifacts "AutoSizeStrategy-Setup-$Version.exe"

        Write-Host "`n--- Publishing to GitHub ---"
        $gh = Get-Command gh -ErrorAction SilentlyContinue
        if (-not $gh) { throw "gh CLI not found. Install via: winget install GitHub.cli" }

        $assets = @()
        if (Test-Path $zipPath) { $assets += $zipPath }
        if (Test-Path $setupExe) { $assets += $setupExe }
        if ($assets.Count -eq 0) { throw "No artifacts found to upload" }

        gh release delete $tag --yes 2>$null
        $assetArgs = $assets | ForEach-Object { """$_""" }
        gh release create $tag @assetArgs --title "AutoSizeStrategy $Version" --notes "See tag $tag for full release notes."
        if ($LASTEXITCODE -ne 0) { throw "GitHub release failed" }

        Write-Host "Published: https://github.com/moravsky/auto-size-strategy-quantower/releases/tag/$tag"
    } finally {
        Write-Host "`n--- Removing worktree ---"
        git worktree remove --force $worktreePath 2>$null
    }

    return
}

# Non-publish path: build the current working tree.
Build-Release -RepoRoot $root -Version $Version | Out-Null

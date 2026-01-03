param(
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Debug"
)

$root = $PSScriptRoot
$source = Join-Path $root "AutoSizeStrategy\bin\$Config"
$dest = "C:\Quantower\Settings\Scripts\Strategies\AutoSizeStrategy"

# Safety checks
if ([string]::IsNullOrWhiteSpace($dest)) {
    Write-Error "Destination path is empty. Aborting."
    exit 1
}

if ($dest -notmatch "Quantower.*AutoSizeStrategy$") {
    Write-Error "Destination path doesn't look right: $dest. Aborting."
    exit 1
}

if (-not (Test-Path $source)) {
    Write-Error "Source path doesn't exist: $source. Did you build first?"
    exit 1
}

# Clean destination
if (Test-Path $dest) {
    Write-Host "Cleaning $dest"
    Remove-Item "$dest\*" -Force -Recurse
}

# Copy new files
$files = @("*.dll", "*.pdb", "*.deps.json")

foreach ($pattern in $files) {
    $items = Get-ChildItem -Path $source -Filter $pattern
    foreach ($item in $items) {
        Copy-Item $item.FullName -Destination $dest -Force
        Write-Host "Deployed: $($item.Name)"
    }
}

Write-Host "[$Config] Deploy complete to $dest"
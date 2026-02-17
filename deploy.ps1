param(
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Debug"
)

$root = $PSScriptRoot
$source = Join-Path $root "AutoSizeStrategy\bin\$Config"

# Determine destination root based on Config
# Debug -> C:\QuantowerDev
# Release -> C:\Quantower
if ($Config -eq "Debug") {
    $destRoot = "C:\QuantowerDev"
} else {
    $destRoot = "C:\Quantower"
}

$dest = "$destRoot\Settings\Scripts\Strategies\AutoSizeStrategy"

# Safety checks
if ([string]::IsNullOrWhiteSpace($dest)) {
    Write-Error "Destination path is empty. Aborting."
    exit 1
}

# Regex check updated to allow both Quantower and QuantowerDev
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
} else {
    # Optional: Create directory if it doesn't exist (useful for new dev setups)
    Write-Host "Destination does not exist, creating: $dest"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
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
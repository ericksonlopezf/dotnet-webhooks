# Copyright © Erickson Lopez. MIT License.
[CmdletBinding()]
param (
    [string]$RootDirectory = "."
)

$targetRoot = (Resolve-Path $RootDirectory).Path
Write-Host "Validating documentation links across repository: $targetRoot" -ForegroundColor Cyan

$mdFiles = Get-ChildItem -Path $targetRoot -Recurse -Filter "*.md" -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git|archive|StrykerOutput)[\\/]'
}

$brokenLinks = @()

foreach ($file in $mdFiles) {
    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    $matches = [regex]::Matches($content, '\[([^\]]+)\]\(([^)]+)\)')

    foreach ($m in $matches) {
        $link = $m.Groups[2].Value.Trim()

        # Disallow local file:/// URIs
        if ($link.StartsWith('file:///')) {
            $brokenLinks += [PSCustomObject]@{
                SourceFile = $file.FullName
                Link = $link
                ResolvedPath = "FORBIDDEN: Local file:/// link is not portable."
            }
            continue
        }

        # Skip web links, mailto, local anchors
        if ($link -match '^(https?://|mailto:|#)') {
            continue
        }

        # Remove anchor if present
        $cleanLink = $link
        if ($cleanLink.Contains('#')) {
            $cleanLink = $cleanLink.Substring(0, $cleanLink.IndexOf('#'))
        }

        if ([string]::IsNullOrWhiteSpace($cleanLink)) {
            continue
        }

        # Resolve path relative to the file's directory
        $fileDir = $file.DirectoryName
        $targetPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($fileDir, $cleanLink))

        if (-not (Test-Path $targetPath)) {
            $brokenLinks += [PSCustomObject]@{
                SourceFile = $file.FullName
                Link = $link
                ResolvedPath = $targetPath
            }
        }
    }
}

if ($brokenLinks.Count -eq 0) {
    Write-Host "  ✅ All relative links across documentation and root files resolve successfully." -ForegroundColor Green
    exit 0
} else {
    Write-Host "  ❌ Found $($brokenLinks.Count) broken link(s):" -ForegroundColor Red
    foreach ($bl in $brokenLinks) {
        Write-Host "    - Source: $($bl.SourceFile)" -ForegroundColor Yellow
        Write-Host "      Target: $($bl.Link)" -ForegroundColor Gray
        Write-Host "      Resolved: $($bl.ResolvedPath)" -ForegroundColor Red
    }
    exit 1
}

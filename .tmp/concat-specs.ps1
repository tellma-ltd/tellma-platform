#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Concatenates every markdown file in docs/specs into one file.

.DESCRIPTION
    Files are appended in name order (0001..0021), each separated from the next
    by exactly three newlines. The output is written as UTF-8 without a BOM.

.PARAMETER SpecsDir
    Folder to read the *.md files from. Defaults to <repo>/docs/specs.

.PARAMETER OutFile
    File to write. Defaults to <repo>/.tmp/specs.md. Overwritten if it exists.

.EXAMPLE
    pwsh .tmp/concat-specs.ps1
#>
[CmdletBinding()]
param(
    [string]$SpecsDir,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

# Resolve the repository root from the script's own location so the script works
# regardless of the caller's current directory.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SpecsDir) { $SpecsDir = Join-Path $repoRoot 'docs' 'specs' }
if (-not $OutFile)  { $OutFile  = Join-Path $repoRoot '.tmp' 'specs.md' }

if (-not (Test-Path -LiteralPath $SpecsDir)) {
    throw "Specs folder not found: $SpecsDir"
}

# Sorted by name so the numeric spec prefixes fix the order.
$files = Get-ChildItem -LiteralPath $SpecsDir -Filter '*.md' -File | Sort-Object Name
if ($files.Count -eq 0) {
    throw "No markdown files found in: $SpecsDir"
}

# Trim each file's trailing newlines, then join with exactly three newlines, so
# the gap between two documents is the same no matter how each file ends.
$parts = foreach ($file in $files) {
    (Get-Content -LiteralPath $file.FullName -Raw).TrimEnd("`r", "`n")
}
$content = ($parts -join ("`n" * 3)) + "`n"

$outDir = Split-Path -Parent $OutFile
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# WriteAllText with an explicit encoding avoids the BOM that Out-File/Set-Content
# can emit on Windows PowerShell.
[System.IO.File]::WriteAllText($OutFile, $content, [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $($files.Count) files to $OutFile ($((Get-Item -LiteralPath $OutFile).Length) bytes)."

# Copyright (c) 2026 Tellma Ltd. All rights reserved.
#
# This source code is licensed under the Apache-2.0 license found in the
# LICENSE file in the root directory of this source tree.

<#
.SYNOPSIS
    Asserts that a publish output contains no EF Core Design-package assemblies (spec Rule 3).

.DESCRIPTION
    The web server (represented in this repo by the BoundaryHost test asset) must never carry
    the EF Design dependency tree: Microsoft.EntityFrameworkCore.Design itself, Roslyn
    (Microsoft.CodeAnalysis*), templating (Mono.TextTemplating*), Humanizer, or Tellma's own
    Design companion. Their presence means the runtime/design boundary leaked.

.PARAMETER PublishDirectory
    The directory produced by `dotnet publish`.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $PublishDirectory)) {
    throw "Publish directory '$PublishDirectory' does not exist."
}

# The deny-list mirrors the dependency tree of Microsoft.EntityFrameworkCore.Design (which is
# developmentDependency=true and must never reach a runtime host).
$denyList = @(
    'Microsoft.EntityFrameworkCore.Design*',
    'Microsoft.CodeAnalysis*',
    'Mono.TextTemplating*',
    'Humanizer*',
    'Tellma.Core.EntityFrameworkCore.Design*'
)

$violations = @()
foreach ($pattern in $denyList) {
    $violations += Get-ChildItem -Path $PublishDirectory -Recurse -File -Filter "$pattern.dll"
}

if ($violations.Count -gt 0) {
    $names = ($violations | ForEach-Object { $_.Name } | Sort-Object -Unique) -join ', '
    throw "Publish boundary violation: Design-tree assemblies found in '$PublishDirectory': $names. " +
        'The runtime host must not reference Tellma.Core.EntityFrameworkCore.Design or Microsoft.EntityFrameworkCore.Design.'
}

# Sanity check: the runtime library itself must be present (otherwise the check checked nothing).
if (-not (Get-ChildItem -Path $PublishDirectory -Recurse -File -Filter 'Tellma.Core.EntityFrameworkCore.dll')) {
    throw "Sanity check failed: Tellma.Core.EntityFrameworkCore.dll not found in '$PublishDirectory'."
}

Write-Host "Publish boundary check passed: no Design-tree assemblies in '$PublishDirectory'."

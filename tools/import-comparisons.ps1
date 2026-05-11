#requires -Version 7
<#
.SYNOPSIS
  Imports per-package comparison numbers from the ZeroAlloc.* ecosystem into docs/za-clean.md.

.DESCRIPTION
  Reads tools/comparison-sources.json, fetches comparison reports from sources where
  status == "live", and splices them between named sentinels in docs/za-clean.md.
  For sources with status "deferred" or "blocked-upstream", writes a placeholder
  block with the tracking URL + note. Idempotent and re-runnable.

.NOTES
  Pattern modeled on ZA.Mapping's tools/import-benchmarks.ps1, extended to multi-source
  via the JSON manifest. Locked-file retries via WriteAllText + bounded loop because
  docs/za-clean.md may be open in an editor/indexer on Windows hosts.
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $repoRoot 'tools/comparison-sources.json'
$perfMd = Join-Path $repoRoot 'docs/za-clean.md'

if (-not (Test-Path $manifestPath)) {
    throw "Manifest not found: $manifestPath"
}
if (-not (Test-Path $perfMd)) {
    throw "Target doc not found: $perfMd"
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$md = Get-Content $perfMd -Raw
$updates = 0

foreach ($source in $manifest.sources) {
    $startMarker = "<!-- $($source.sentinel):START -->"
    $endMarker = "<!-- $($source.sentinel):END -->"
    $pattern = "$([regex]::Escape($startMarker))[\s\S]*?$([regex]::Escape($endMarker))"

    if ($md -notmatch $pattern) {
        Write-Warning "Sentinels for $($source.name) ($($source.sentinel)) not found in docs/za-clean.md; skipping."
        continue
    }

    $body = switch ($source.status) {
        'live' {
            try {
                $remote = Invoke-WebRequest -Uri $source.url -UseBasicParsing
                $innerPattern = "$([regex]::Escape($source.extract_start))([\s\S]*?)$([regex]::Escape($source.extract_end))"
                if ($remote.Content -match $innerPattern) {
                    $extracted = $Matches[1].Trim()
                    "_Imported from $($source.name) — last refreshed $(Get-Date -Format 'yyyy-MM-dd')._`n`n$extracted"
                } else {
                    "_$($source.name) source did not yield comparison content (inner sentinels not matched). Check $($source.url)._"
                }
            } catch {
                "_Failed to import $($source.name): $($_.Exception.Message)._"
            }
        }
        'deferred' {
            "_**Status: deferred.** $($source.note) Tracking: $($source.tracking_url)._"
        }
        'blocked-upstream' {
            "_**Status: blocked upstream.** $($source.note) Tracking: $($source.tracking_url)._"
        }
        default {
            "_**Status: unknown ($($source.status)).** Manifest entry for $($source.name) has an unrecognized status._"
        }
    }

    $wrapped = "$startMarker`n$body`n$endMarker"

    # Use MatchEvaluator to avoid $-substitution issues in table content.
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] { param($m) $wrapped }
    $md = [regex]::Replace($md, $pattern, $evaluator)
    $updates++
}

# Retry write — docs/za-clean.md may be transiently locked by an editor/indexer.
$attempts = 0
while ($true) {
    try {
        [System.IO.File]::WriteAllText($perfMd, $md, [System.Text.UTF8Encoding]::new($false))
        break
    } catch [System.IO.IOException] {
        $attempts++
        if ($attempts -ge 10) { throw }
        Start-Sleep -Milliseconds 500
    }
}

Write-Host "Updated $perfMd from $updates / $($manifest.sources.Count) sources."

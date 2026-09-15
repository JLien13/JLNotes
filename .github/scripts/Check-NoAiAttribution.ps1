<#
.SYNOPSIS
  Fails when any commit in a range, the pull-request text, or the added lines of the
  diff carry AI attribution (Claude co-author lines, Claude session links, Anthropic
  addresses, "Generated with Claude" footers).

.DESCRIPTION
  Checks, for every commit in BaseSha..HeadSha:
    - author name and email, committer name and email
    - the full commit message
  Checks the pull-request title and body when supplied.
  Checks every added line of the diff between BaseSha and HeadSha, except the files
  that legitimately name the forbidden strings (the repository rules and this check).

  Exit 0 when clean, exit 1 with one line per hit otherwise.

.EXAMPLE
  .\Check-NoAiAttribution.ps1 -BaseSha origin/Dev/1.2.8 -HeadSha HEAD
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $BaseSha,
    [Parameter(Mandatory = $true)] [string] $HeadSha,
    [string] $PrTitle = '',
    [string] $PrBody = ''
)

$ErrorActionPreference = 'Stop'

# One regex per thing we refuse. Case-insensitive.
$patterns = @(
    @{ Name = 'Claude co-author trailer';      Regex = 'co-authored-by:.*(claude|anthropic)' },
    @{ Name = 'Claude session trailer';        Regex = 'claude-session:' },
    @{ Name = 'Claude session link';           Regex = 'claude\.ai/code' },
    @{ Name = 'Anthropic address';             Regex = 'anthropic\.com' },
    @{ Name = 'Generated-with-Claude footer';  Regex = 'generated with.*claude' }
)

# Files allowed to contain the forbidden strings because they describe the rule.
$diffExclusions = @(
    'CLAUDE.md',
    '.github/scripts/Check-NoAiAttribution.ps1',
    '.github/workflows/pr-no-ai-attribution.yml'
)

$hits = New-Object System.Collections.Generic.List[string]

function Test-Text {
    param([string] $Where, [string] $Text)
    if ([string]::IsNullOrEmpty($Text)) { return }
    $lines = $Text -split "`r?`n"
    foreach ($line in $lines) {
        foreach ($p in $patterns) {
            if ($line -imatch $p.Regex) {
                $hits.Add(("{0}: {1}: {2}" -f $Where, $p.Name, $line.Trim()))
                break
            }
        }
    }
}

# --- commits -------------------------------------------------------------------
$range = "$BaseSha..$HeadSha"
$shas = @(git rev-list --no-merges $range 2>$null)
if ($LASTEXITCODE -ne 0) { throw "git rev-list failed for range $range" }
Write-Host ("Checking {0} commit(s) in {1}" -f $shas.Count, $range)

foreach ($sha in $shas) {
    $short = $sha.Substring(0, 7)
    $meta = git show -s --format='%an <%ae>%n%cn <%ce>' $sha
    Test-Text -Where "commit $short identity" -Text ($meta -join "`n")
    $body = git show -s --format='%B' $sha
    Test-Text -Where "commit $short message" -Text ($body -join "`n")
}

# --- pull-request text ---------------------------------------------------------
Test-Text -Where 'pull-request title' -Text $PrTitle
Test-Text -Where 'pull-request body'  -Text $PrBody

# --- added lines in the diff ---------------------------------------------------
$diff = git diff --unified=0 --no-color "$BaseSha...$HeadSha" 2>$null
if ($LASTEXITCODE -ne 0) { throw "git diff failed for $BaseSha...$HeadSha" }
$file = ''
$skip = $false
foreach ($line in $diff) {
    if ($line -like '+++ b/*') {
        $file = $line.Substring(6)
        $skip = $diffExclusions -contains $file
        continue
    }
    if ($skip) { continue }
    if ($line -like '+*' -and -not ($line -like '+++*')) {
        Test-Text -Where "diff $file" -Text $line.Substring(1)
    }
}

# --- verdict -------------------------------------------------------------------
if ($hits.Count -gt 0) {
    Write-Host ''
    Write-Host ("[FAIL] {0} AI attribution hit(s):" -f $hits.Count)
    foreach ($h in $hits) { Write-Host ("  * {0}" -f $h) }
    Write-Host ''
    Write-Host 'Rewrite the commit message(s) and pull-request text without the attribution, then push again.'
    exit 1
}

Write-Host '[ OK ] No AI attribution in commits, pull-request text, or added lines.'
exit 0

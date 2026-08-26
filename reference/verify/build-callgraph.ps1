<#
.SYNOPSIS
Build a call-graph index from ctags: every procedure/function definition,
plus every call site that references it.

Generates repo_symbols.json and tags as intermediate ctags output, then
deletes both once callgraph_index.json (name -> definition + call sites)
is written -- that's the only file this script leaves behind.

Note: this does NOT use `global`/GNU Global. Global's ctags-plugin backend
only extracts definitions (GTAGS), not references (GRTAGS) -- reference
extraction there requires the native C/C++/Java parser or the pygments
backend, neither of which covers Pascal without a fragile extra Python/
pygments install. Call sites are instead found with a direct identifier
scan across the source, which needs nothing beyond ctags.
#>

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$srcDir = Join-Path $PSScriptRoot '..\DOSAnacreonSource131'
$indexFile = Join-Path $PSScriptRoot 'dos_131_callgraph.json'

if (-not (Test-Path $srcDir)) {
    Write-Error "Source directory not found: $srcDir"
    exit 1
}

$ctags = & { where.exe ctags 2>$null }
if (-not $ctags) {
    Write-Error "ctags not found in PATH. Install universal-ctags."
    exit 1
}
Write-Host "ctags: $ctags"
Write-Host ""

# Generate ctags symbols (JSON, for human inspection)
Write-Host "Generating ctags in $srcDir..."
Push-Location $srcDir
try {
    Remove-Item -Force -ErrorAction SilentlyContinue 'repo_symbols.json'
    & ctags `
        --fields=+t `
        --extras=+q `
        --output-format=json `
        --sort=no `
        --languages=pascal `
        -o repo_symbols.json `
        *.PAS
    Write-Host "✓ repo_symbols.json generated"

    # Also generate ex-ctags format 'tags' file with an explicit line:NNN
    # field -- the JSON output above doesn't reliably carry line numbers.
    Remove-Item -Force -ErrorAction SilentlyContinue 'tags'
    & ctags `
        --fields=+neKSC `
        --extras=+q `
        --sort=no `
        --languages=pascal `
        -o tags `
        *.PAS
    Write-Host "✓ tags generated"
} finally {
    Pop-Location
}

# Read symbols
Write-Host ""
Write-Host "Parsing tags..."

$symbols = @{}
$symbolsList = @()

# Format (from --fields=+neKSC):
#   name<TAB>file<TAB>pattern;"<TAB>kind<TAB>line:NNN<TAB>signature:(...)
# kind is the bare full-name word (procedure/function/...); line and
# signature are key:value fields that can appear in any order after it.
$tagsFile = Join-Path $srcDir 'tags'
$tagLines = Get-Content $tagsFile | Where-Object { -not $_.StartsWith('!') }

foreach ($tagLine in $tagLines) {
    if ([string]::IsNullOrWhiteSpace($tagLine)) { continue }

    $parts = $tagLine -split "`t"
    if ($parts.Count -lt 4) { continue }

    $name = $parts[0]
    $file = $parts[1]
    $kind = $parts[3]

    $line = $null
    $signature = ""

    for ($i = 4; $i -lt $parts.Count; $i++) {
        $field = $parts[$i]
        if ($field -match '^line:(\d+)$') {
            $line = [int]$matches[1]
        }
        elseif ($field -match '^signature:(.*)$') {
            $signature = $matches[1]
        }
    }

    if ($kind -in @('procedure', 'function')) {
        $key = "$name|$file|$line"
        if (-not $symbols.ContainsKey($key)) {
            $symbols[$key] = @{
                name      = $name
                path      = $file
                line      = $line
                kind      = $kind
                signature = $signature
            }
            $symbolsList += $symbols[$key]
        }
    }
}

Write-Host "Found $($symbolsList.Count) unique procedure/function definitions"

# Pascal identifiers are case-insensitive, so a call site may not match the
# declared casing -- look symbols up case-insensitively, keyed to the
# canonical (declared) name.
$symbolSet = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($sym in $symbolsList) { $symbolSet[$sym.name] = $sym.name }

# Definition lines per symbol, so a decl/impl header line isn't counted as
# a call site to itself.
$defLines = @{}
foreach ($sym in $symbolsList) {
    if (-not $defLines.ContainsKey($sym.name)) {
        $defLines[$sym.name] = [System.Collections.Generic.HashSet[string]]::new()
    }
    [void]$defLines[$sym.name].Add("$($sym.path)|$($sym.line)")
}

$references = @{}
foreach ($name in $symbolSet.Values | Select-Object -Unique) {
    $references[$name] = [System.Collections.Generic.List[object]]::new()
}

# Single pass over every source line: tokenize identifiers, look each up
# against known symbol names. O(total lines) instead of O(symbols x files).
Write-Host ""
Write-Host "Scanning source for call sites..."

$identRegex = [regex]'[A-Za-z_][A-Za-z0-9_]*'
$pasFiles = Get-ChildItem $srcDir -Filter '*.PAS' | Sort-Object Name

# Blank out comment bodies before tokenizing, so a symbol name mentioned in
# a `{ Comment }` or `(* comment *)` block isn't counted as a call site.
# Runs on the whole file (RegexOptions.Singleline so `.` spans comments
# that cross lines) and replaces non-newline comment characters with
# spaces, so line numbers stay aligned with the original file.
# Known gap: doesn't understand string literals, so a '...{...' string
# would be mistaken for a comment start -- rare enough in this source to
# not be worth a full Pascal lexer here.
function Strip-PascalComments([string]$text) {
    $opts = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $blank = { param($m) $m.Value -replace '[^\n]', ' ' }
    $text = [regex]::Replace($text, '\{[^}]*\}', $blank, $opts)
    $text = [regex]::Replace($text, '\(\*.*?\*\)', $blank, $opts)
    return $text
}

for ($fi = 0; $fi -lt $pasFiles.Count; $fi++) {
    $f = $pasFiles[$fi]
    Write-Progress -Activity "Scanning source for call sites" `
        -Status "$($f.Name) ($($fi + 1)/$($pasFiles.Count))" `
        -PercentComplete ([int](($fi + 1) / $pasFiles.Count * 100))

    $rawText = Get-Content $f.FullName -Raw
    $lines = (Strip-PascalComments $rawText) -split '\r?\n'
    for ($li = 0; $li -lt $lines.Count; $li++) {
        $lineNo = $li + 1
        $text = $lines[$li]

        foreach ($m in $identRegex.Matches($text)) {
            $canonicalName = $null
            if (-not $symbolSet.TryGetValue($m.Value, [ref]$canonicalName)) { continue }

            $lineKey = "$($f.Name)|$lineNo"
            if ($defLines[$canonicalName].Contains($lineKey)) { continue }

            $references[$canonicalName].Add(@{
                file    = $f.Name
                line    = $lineNo
                context = $text.Trim()
            })
        }
    }
}

Write-Progress -Activity "Scanning source for call sites" -Completed

# Assemble final index
$indexData = @{}
foreach ($sym in $symbolsList) {
    $refs = $references[$sym.name]
    $indexData[$sym.name] = @{
        file       = $sym.path
        line       = $sym.line
        kind       = $sym.kind
        signature  = $sym.signature
        refCount   = $refs.Count
        references = $refs
    }
}

Write-Host ""
Write-Host "Writing call-graph index to $indexFile..."
$indexData | ConvertTo-Json -Depth 10 | Set-Content $indexFile
Write-Host "✓ callgraph_index.json written"

Write-Host ""
Write-Host "Removing intermediate ctags output..."
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $srcDir 'repo_symbols.json')
Remove-Item -Force -ErrorAction SilentlyContinue $tagsFile
Write-Host "✓ repo_symbols.json and tags removed"

Write-Host ""
Write-Host "Call-graph analysis complete."
Write-Host ""
Write-Host "To query, try:"
Write-Host "  (Get-Content $indexFile | ConvertFrom-Json).SymbolName.references"
Write-Host ""

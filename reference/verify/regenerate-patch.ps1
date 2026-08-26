<#
.SYNOPSIS
Regenerates one patches/<File>.patch from reference/verify/patched/<File> against the
pristine reference/DOSAnacreonSource131/<File>.

This is the "Restoring a removed procedure" / "Trimming a unit down to size" process from
README.md, scripted: hand-driving git diff --no-index through a shell repeatedly produced
patches with the wrong a/b path prefix (git diff --no-index always adds its own a/ b/ on
top of whatever path you give it) or corrupted line endings (a PowerShell `>`/Out-File
redirect defaults to UTF-16LE; some Bash/MSYS pipes silently rewrite CRLF<->LF) -- both
silent failures that only surface later as a `git apply` error or, worse, a patch that
applies but reproduces the wrong bytes. This script gets the byte-for-byte plumbing right
every time and self-verifies the result, so regenerating a patch stops being a multi-step
manual dance.

patched/<File> must already contain the hand-edited target state (edit it directly, same
as build.ps1's own workflow: run build.ps1 once to populate patched/, hand-edit the file(s)
you're changing, confirm `fpc -Mtp -CfSSE2 runworld.pas` still compiles from inside
patched/, then run this script).

.PARAMETER File
Pascal source file name, e.g. INTRFACE.PAS. Must exist under both
reference/DOSAnacreonSource131/ (pristine) and reference/verify/patched/ (hand-edited).

.PARAMETER OutDir
Directory to write <File>.patch into. Defaults to reference/verify/patches/, the real
committed patch set. Pass a scratch directory to try a regeneration without touching the
committed patches -- e.g. to sanity-check a large rewrite before trusting it.

.PARAMETER PristineDir
Directory holding the untouched original source. Defaults to reference/DOSAnacreonSource131,
the one true pristine copy -- override only for a different build lane's own hand-edited
copy (e.g. reference/verify/fullbuild/scratch) that still diffs against the same pristine.

.PARAMETER PatchedDir
Directory holding the hand-edited target state. Defaults to reference/verify/patched/ (this
lane's own disposable build output) -- override to point at another lane's scratch dir, e.g.
reference/verify/fullbuild/scratch, so that lane can reuse this script's byte-plumbing instead
of duplicating it.

.EXAMPLE
./regenerate-patch.ps1 -File INTRFACE.PAS

.EXAMPLE
./regenerate-patch.ps1 -File INTRFACE.PAS -OutDir $env:TEMP\patch-scratch

.EXAMPLE
./regenerate-patch.ps1 -File STRG.PAS -PatchedDir fullbuild\scratch -OutDir fullbuild\patches
#>

param(
    [Parameter(Mandatory)]
    [string]$File,

    [string]$OutDir = (Join-Path $PSScriptRoot 'patches'),
    [string]$PristineDir = (Join-Path $PSScriptRoot '..\DOSAnacreonSource131'),
    [string]$PatchedDir = (Join-Path $PSScriptRoot 'patched')
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# A relative -OutDir/-PristineDir/-PatchedDir resolves against PowerShell's own $PWD, but the
# .NET file APIs below (ReadAllBytes, File.WriteAllBytes) resolve relative paths against the
# process's Environment.CurrentDirectory instead, which Set-Location does not keep in sync --
# a bare relative path here silently resolves against the wrong directory. Convert to absolute
# via PowerShell's own path provider (not Resolve-Path, which requires the path to already exist).
function Resolve-PSPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}
$OutDir = Resolve-PSPath $OutDir
$PristineDir = Resolve-PSPath $PristineDir
$PatchedDir = Resolve-PSPath $PatchedDir

$pristine = Join-Path $PristineDir $File
$patched  = Join-Path $PatchedDir $File

if (-not (Test-Path $pristine)) { throw "No pristine source: $pristine" }
if (-not (Test-Path $patched))  { throw "No patched\$File -- run build.ps1, then hand-edit patched\$File, before regenerating its patch." }

function Get-GitDiffBytes([string]$WorkDir, [string]$RelFile) {
    # Drive git through Process + raw stdout bytes, not a PowerShell pipe/redirect -- see
    # this script's own header comment for why that's the part that kept going wrong.
    $psi = [System.Diagnostics.ProcessStartInfo]::new('git')
    foreach ($a in @('diff', '--no-index', '--no-prefix', '--', "a/$RelFile", "b/$RelFile")) {
        $psi.ArgumentList.Add($a)
    }
    $psi.WorkingDirectory = $WorkDir
    $psi.RedirectStandardOutput = $true
    $psi.UseShellExecute = $false

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = [System.IO.MemoryStream]::new()
    $proc.StandardOutput.BaseStream.CopyTo($stdout)
    $proc.WaitForExit()
    # git diff --no-index exits 1 when it finds differences -- that's the expected case here,
    # not a failure. Only >1 (e.g. 128, a real git error) is one.
    if ($proc.ExitCode -gt 1) {
        throw "git diff --no-index failed with exit code $($proc.ExitCode) in $WorkDir"
    }
    return $stdout.ToArray()
}

function Strip-DiffGitHeader([byte[]]$Bytes) {
    # Drop the leading "diff --git a/File b/File" + "index ..." lines -- every other patch
    # in patches/ already omits them (git apply -p1 doesn't need them). Slice at the byte
    # offset just past the second LF so nothing downstream is touched or re-encoded.
    $lf = [byte]10
    $first = [Array]::IndexOf($Bytes, $lf)
    if ($first -lt 0) { throw 'git diff output has no line breaks -- unexpected format.' }
    $second = [Array]::IndexOf($Bytes, $lf, $first + 1)
    if ($second -lt 0) { throw 'git diff output has only one line -- unexpected format.' }
    return $Bytes[($second + 1)..($Bytes.Length - 1)]
}

# git diff --no-index always prepends its own a/ b/ prefix on top of whatever paths you
# give it -- to land on the plain "--- a/File" / "+++ b/File" form every committed patch
# uses (needed for `git apply -p1`), stage both files under literal a/<File> and b/<File>
# in a scratch dir and pass --no-prefix so git doesn't double that prefix up.
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) "regenerate-patch-$([guid]::NewGuid())"
$aDir = Join-Path $scratch 'a'
$bDir = Join-Path $scratch 'b'
New-Item -ItemType Directory -Path $aDir -Force | Out-Null
New-Item -ItemType Directory -Path $bDir -Force | Out-Null
try {
    Copy-Item $pristine (Join-Path $aDir $File)
    Copy-Item $patched (Join-Path $bDir $File)

    $rawBytes = Get-GitDiffBytes -WorkDir $scratch -RelFile $File
    if ($rawBytes.Length -eq 0) {
        throw "patched\$File is byte-identical to pristine -- nothing to regenerate (delete patches\$File.patch instead if that's really intended)."
    }
    $body = Strip-DiffGitHeader $rawBytes

    # Self-verify before writing anywhere real: apply the freshly generated patch to a
    # clean pristine copy and confirm it reproduces patched\<File> byte-for-byte. This is
    # the same check "Restoring a removed procedure" in README.md describes doing by hand
    # after every regeneration -- doing it here means a bad patch never reaches disk.
    $verifyDir = Join-Path $scratch 'verify'
    New-Item -ItemType Directory -Path $verifyDir -Force | Out-Null
    $verifyFile = Join-Path $verifyDir $File
    Copy-Item $pristine $verifyFile
    $patchFile = Join-Path $scratch "$File.patch"
    [System.IO.File]::WriteAllBytes($patchFile, $body)

    & git -C $verifyDir apply -p1 --verbose $patchFile
    if ($LASTEXITCODE -ne 0) {
        throw "Regenerated patch for $File does not apply cleanly to pristine source -- not writing it. See git apply's output above."
    }

    $verifyBytes = [System.IO.File]::ReadAllBytes($verifyFile)
    $patchedBytes = [System.IO.File]::ReadAllBytes($patched)
    if (-not [System.Linq.Enumerable]::SequenceEqual($verifyBytes, $patchedBytes)) {
        throw "Regenerated patch for $File applies but does not reproduce patched\$File byte-for-byte -- not writing it."
    }

    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    $outPath = Join-Path $OutDir "$File.patch"
    [System.IO.File]::WriteAllBytes($outPath, $body)
    Write-Host "Wrote $outPath ($($body.Length) bytes), verified: applies cleanly to pristine and reproduces patched\$File byte-for-byte."
} finally {
    Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

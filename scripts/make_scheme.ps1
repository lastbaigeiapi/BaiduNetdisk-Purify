# Build the patch scheme for ONE Baidu Netdisk build:  schemes\<Name>\{patchset.json, components.json, files\*}
# Matching is done by the SHA-256 of the original core.asar only; <Name> is just a human-readable folder label.
# By default the cross-version rules in scripts\portable\rules\ are resolved for this build (scripts\portable\build_patchset.js);
# -Def <legacy definition> uses the old single-build generator instead (scripts\gen_patchset.js).
# The expected output hash is measured by running the real PowerShell patcher (Windows PowerShell 5.1, like on users' PCs).
# Usage: powershell -File scripts\make_scheme.ps1 -Name 8.9.7.101 -CoreAsar <original core.asar> -Components <components.json> [-Tools <dir>] [-Def <file>] [-OutputRoot <dir>]
# Afterwards run scripts\sign_schemes.ps1 to rebuild and sign schemes\index.json.   (ASCII-only source)
param(
  [Parameter(Mandatory = $true)][ValidatePattern('^[a-zA-Z0-9](?:[a-zA-Z0-9._-]{0,62}[a-zA-Z0-9_-])?$')][string]$Name,
  [Parameter(Mandatory = $true)][string]$CoreAsar,
  [Parameter(Mandatory = $true)][string]$Components,
  [string]$Tools,
  [string]$Def,
  [string]$OutputRoot
)
if ($PSVersionTable.PSEdition -ne 'Core') { $env:PSModulePath = (Join-Path $env:SystemRoot 'system32\WindowsPowerShell\v1.0\Modules') + ';' + (Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules') }
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$CoreAsar = (Resolve-Path -LiteralPath $CoreAsar).Path
$Components = (Resolve-Path -LiteralPath $Components).Path
if ($Tools) { $Tools = (Resolve-Path -LiteralPath $Tools).Path }
if ($Def) { $Def = (Resolve-Path -LiteralPath $Def).Path }
if ($OutputRoot) { $OutputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot) }
Set-Location -LiteralPath $repo
if (-not $Tools) { $Tools = Join-Path $repo 'tools' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'schemes' }
function Sha([string]$p) { $fs = [IO.File]::OpenRead($p); $h = [Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($h.ComputeHash($fs)) -replace '-', '').ToLowerInvariant() } finally { $h.Dispose(); $fs.Dispose() } }
$dest = Join-Path $OutputRoot $Name
[void][IO.Directory]::CreateDirectory($OutputRoot)
$nonce = [guid]::NewGuid().ToString('N')
$dir = Join-Path $OutputRoot ('.build-' + $nonce)
$previous = Join-Path $OutputRoot ('.previous-' + $nonce)
$patchset = Join-Path $dir 'patchset.json'
$tmp = Join-Path $dir 'patched.tmp'
$orig = Sha $CoreAsar
Write-Output "scheme $Name  original core.asar $orig"

function Remove-BuildDirectory([string]$Path, [string]$Prefix) {
  if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
  $resolved = (Resolve-Path -LiteralPath $Path).ProviderPath
  $parent = (Resolve-Path -LiteralPath $OutputRoot).ProviderPath.TrimEnd('\')
  if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved) -eq ($Prefix + $nonce)) {
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}

try {
  [void][IO.Directory]::CreateDirectory((Join-Path $dir 'files'))
  # '-' means unmeasured. PowerShell 5.1 drops empty arguments to native tools.
  if ($Def) { & node scripts\gen_patchset.js $patchset $orig '-' $Def $Name }
  else { & node scripts\portable\build_patchset.js $CoreAsar $Tools $patchset $orig '-' $Name }
  if ($LASTEXITCODE) { throw 'patchset generation failed' }
  $set = [IO.File]::ReadAllText($patchset) | ConvertFrom-Json
  foreach ($nf in $set.newFiles) {
    Copy-Item -LiteralPath (Join-Path $repo ('patch\' + $nf)) -Destination (Join-Path $dir ('files\' + $nf))
  }
  Copy-Item -LiteralPath $Components -Destination (Join-Path $dir 'components.json')

  $variants = @('patchset.json')
  if (-not $Def) {
    $variants += 'video-download.json'
    & node scripts\portable\build_patchset.js $CoreAsar $Tools (Join-Path $dir 'video-download.json') $orig '-' $Name --download-videos
    if ($LASTEXITCODE) { throw 'video download patchset generation failed' }
  }
  $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
  foreach ($variant in $variants) {
    & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'installer\payload\nd_patcher.ps1') -CoreAsar $CoreAsar -OutAsar $tmp -PatchDir $dir -PatchSetName $variant
    if ($LASTEXITCODE -or -not (Test-Path -LiteralPath $tmp)) { throw "nd_patcher failed for $variant" }
    $variantPath = Join-Path $dir $variant
    $definition = [IO.File]::ReadAllText($variantPath) | ConvertFrom-Json
    $definition.expectedOutputSha256 = Sha $tmp
    [IO.File]::Delete($tmp)
    [IO.File]::WriteAllText($variantPath, (ConvertTo-Json -InputObject $definition -Depth 16), [Text.UTF8Encoding]::new($false))
    Write-Output "scheme $Name  $variant output $($definition.expectedOutputSha256)"
  }
  if (Test-Path -LiteralPath $dest -PathType Container) {
    $existing = Get-Item -LiteralPath $dest -Force
    if ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "refusing to replace a linked scheme directory: $dest" }
    [IO.Directory]::Move($dest, $previous)
  }
  try {
    [IO.Directory]::Move($dir, $dest)
  } catch {
    if ((Test-Path -LiteralPath $previous) -and -not (Test-Path -LiteralPath $dest)) {
      [IO.Directory]::Move($previous, $dest)
    }
    throw
  }
  Remove-BuildDirectory $previous '.previous-'
} finally {
  Remove-BuildDirectory $dir '.build-'
}

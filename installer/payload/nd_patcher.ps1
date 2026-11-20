# BaiduNetdisk de-marketing -- core.asar patcher (Windows PowerShell 5.1, no other runtime needed)
# Applies patchset.json to the user's OWN official core.asar. Deterministic:
#   * header JSON is edited textually (never re-serialized)
#   * modified/new files are appended to the data section; original bytes are untouched
#   * every anchor must match exactly once, otherwise nothing is written
# Usage: nd_patcher.ps1 -CoreAsar <in> -OutAsar <out> -PatchDir <dir with patchset.json + files\>
#        [-CheckOnly]   only verify that every anchor matches; write nothing
# Exit code 0 = success.  Source is ASCII-only on purpose (PS 5.1 reads BOM-less scripts as ANSI).
param(
  [Parameter(Mandatory = $true)][string]$CoreAsar,
  [string]$OutAsar,
  [Parameter(Mandatory = $true)][string]$PatchDir,
  [switch]$CheckOnly,
  [ValidateSet('patchset.json', 'video-download.json')][string]$PatchSetName = 'patchset.json'
)
# A child of PowerShell 7 inherits its PSModulePath, which breaks module autoloading in Windows PowerShell 5.1
# (cmdlets such as Get-FileHash / Register-ScheduledTask then appear to be missing). Reset to the 5.1 defaults.
if ($PSVersionTable.PSEdition -ne 'Core') { $env:PSModulePath = (Join-Path $env:SystemRoot 'system32\WindowsPowerShell\v1.0\Modules') + ';' + (Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules') }
$ErrorActionPreference = 'Stop'
$Utf8 = New-Object System.Text.UTF8Encoding($false, $true)   # no BOM, throw on invalid bytes
$BlockSize = 4194304
$NUL = [string][char]0

function Get-Sha256Hex([byte[]]$Bytes, [int]$Offset, [int]$Count) {
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try { $h = $sha.ComputeHash($Bytes, $Offset, $Count) } finally { $sha.Dispose() }
  return ([System.BitConverter]::ToString($h) -replace '-', '').ToLowerInvariant()
}
function Get-FileSha256([string]$Path) {
  $fs = [System.IO.File]::OpenRead($Path); $h = [System.Security.Cryptography.SHA256]::Create()
  try { return ([System.BitConverter]::ToString($h.ComputeHash($fs)) -replace '-', '').ToLowerInvariant() } finally { $h.Dispose(); $fs.Dispose() }
}
function New-EntryJson([byte[]]$Bytes, [int64]$Offset) {
  $blocks = New-Object System.Collections.Generic.List[string]
  for ($i = 0; $i -lt $Bytes.Length; $i += $BlockSize) {
    $n = [Math]::Min($BlockSize, $Bytes.Length - $i)
    $blocks.Add('"' + (Get-Sha256Hex $Bytes $i $n) + '"')
  }
  if ($Bytes.Length -eq 0) { $blocks.Add('"' + (Get-Sha256Hex $Bytes 0 0) + '"') }
  $whole = Get-Sha256Hex $Bytes 0 $Bytes.Length
  return '{"size":' + $Bytes.Length + ',"offset":"' + $Offset + '","integrity":{"algorithm":"SHA256","hash":"' + $whole +
         '","blockSize":' + $BlockSize + ',"blocks":[' + ($blocks -join ',') + ']}}'
}
function Get-OrdinalCount([string]$S, [string]$F) {
  $n = 0; $i = 0
  while (($i = $S.IndexOf($F, $i, [System.StringComparison]::Ordinal)) -ge 0) { $n++; $i += [Math]::Max(1, $F.Length) }
  return $n
}

# ---------------- load patchset ----------------
$psPath = Join-Path $PatchDir $PatchSetName
$set = [System.IO.File]::ReadAllText($psPath, $Utf8) | ConvertFrom-Json
if ($set.format -ne 1) { throw "unsupported patchset format: $($set.format)" }
$MARK = [string]$set.mark

# ---------------- read asar header ----------------
$fs = [System.IO.File]::Open($CoreAsar, 'Open', 'Read', 'Read')
$br = New-Object System.IO.BinaryReader($fs)
try {
  $u0 = $br.ReadUInt32(); $u1 = $br.ReadUInt32(); $u2 = $br.ReadUInt32(); $u3 = $br.ReadUInt32()
  if ($u0 -ne 4 -or $u2 -ne ($u1 - 4) -or $u3 -gt $u2) { throw 'not a valid asar archive (unexpected pickle header)' }
  $dataStart = 8 + [int64]$u1
  $hdrText = $Utf8.GetString($br.ReadBytes([int]$u3))
  if (-not $hdrText.StartsWith('{"files":{')) { throw 'unexpected asar header layout' }
  $hdr = $hdrText | ConvertFrom-Json

  # ---------------- apply patches (in memory) ----------------
  $texts = [ordered]@{}        # file -> patched text
  $origHash = @{}              # file -> original integrity hash
  $applied = 0
  foreach ($p in $set.patches) {
    $f = [string]$p.file
    if (-not $texts.Contains($f)) {
      $e = $hdr.files.$f
      if ($null -eq $e -or $null -eq $e.offset) { throw "file not found in archive: $f" }
      $fs.Position = $dataStart + [int64]$e.offset
      $raw = $br.ReadBytes([int]$e.size)
      $texts[$f] = $Utf8.GetString($raw)
      $origHash[$f] = [string]$e.integrity.hash
      if ($f -eq 'main.js' -and $texts[$f].Contains($MARK)) { throw 'this core.asar is already patched; use the original file' }
    }
    $s = [string]$texts[$f]
    if ($p.kind -eq 'literal') {
      $find = [string]$p.find
      $n = Get-OrdinalCount $s $find
      if ($n -ne 1) { throw ("anchor count {0} (expected 1): {1} [{2}]" -f $n, $f, $p.name) }
      $i = $s.IndexOf($find, [System.StringComparison]::Ordinal)
      $s = $s.Substring(0, $i) + [string]$p.repl + $s.Substring($i + $find.Length)
    } elseif ($p.kind -eq 'regex') {
      $rx = New-Object System.Text.RegularExpressions.Regex([string]$p.pattern, [System.Text.RegularExpressions.RegexOptions]::ECMAScript)
      $ms = $rx.Matches($s)
      if ($ms.Count -ne 1) { throw ("regex match count {0} (expected 1): {1} [{2}]" -f $ms.Count, $f, $p.name) }
      $m = $ms[0]
      $repl = [string]$p.template
      for ($g = [int]$p.groups; $g -ge 0; $g--) { $repl = $repl.Replace($NUL + 'G' + $g + $NUL, $m.Groups[$g].Value) }
      if ($repl.Contains($NUL)) { throw "unresolved template placeholder: $f [$($p.name)]" }
      $s = $s.Substring(0, $m.Index) + $repl + $s.Substring($m.Index + $m.Length)
    } else { throw "unknown patch kind: $($p.kind)" }
    $texts[$f] = $s
    $applied++
  }
  Write-Host ("anchors OK: {0}/{1} patches across {2} files" -f $applied, $set.patches.Count, $texts.Count)
  if ($CheckOnly) { exit 0 }
  if (-not $OutAsar) { throw '-OutAsar is required unless -CheckOnly' }

  # ---------------- plan appended data + header edits ----------------
  $dataLen = $fs.Length - $dataStart
  $append = New-Object System.Collections.Generic.List[object]   # @{ name; bytes }
  $newHdr = $hdrText
  foreach ($f in $texts.Keys) {
    $bytes = $Utf8.GetBytes([string]$texts[$f])
    $entry = New-EntryJson $bytes ($dataLen)
    $needle = '"hash":"' + $origHash[$f] + '"'
    if ((Get-OrdinalCount $newHdr $needle) -ne 1) { throw "header entry for $f not unique" }
    $hi = $newHdr.IndexOf($needle, [System.StringComparison]::Ordinal)
    $si = $newHdr.LastIndexOf('{"size":', $hi, [System.StringComparison]::Ordinal)
    $key = '"' + $f + '":'
    if ($si -lt $key.Length -or $newHdr.Substring($si - $key.Length, $key.Length) -ne $key) { throw "header entry key mismatch for $f" }
    $ei = $newHdr.IndexOf(']}}', $hi, [System.StringComparison]::Ordinal)
    if ($ei -lt 0) { throw "header entry end not found for $f" }
    $newHdr = $newHdr.Substring(0, $si) + $entry + $newHdr.Substring($ei + 3)
    $append.Add(@{ name = $f; bytes = $bytes }); $dataLen += $bytes.Length
  }
  $insert = ''
  foreach ($nf in $set.newFiles) {
    if ($null -ne $hdr.files.$nf) { throw "archive already contains $nf" }
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path (Join-Path $PatchDir 'files') $nf))
    [void]$Utf8.GetString($bytes)   # must be valid UTF-8
    $insert += '"' + $nf + '":' + (New-EntryJson $bytes $dataLen) + ','
    $append.Add(@{ name = $nf; bytes = $bytes }); $dataLen += $bytes.Length
  }
  $newHdr = '{"files":{' + $insert + $newHdr.Substring(10)
  [void]($newHdr | ConvertFrom-Json)   # header must still be valid JSON

  # ---------------- write output ----------------
  $hb = $Utf8.GetBytes($newHdr)
  $pad = (4 - ($hb.Length % 4)) % 4
  $payload = 4 + $hb.Length + $pad
  $tmp = $OutAsar + '.partial'
  $out = [System.IO.File]::Open($tmp, 'Create', 'Write', 'None')
  $bw = New-Object System.IO.BinaryWriter($out)
  try {
    $bw.Write([uint32]4); $bw.Write([uint32](4 + $payload)); $bw.Write([uint32]$payload); $bw.Write([uint32]$hb.Length)
    $bw.Write($hb); if ($pad) { $bw.Write((New-Object byte[] $pad)) }
    $bw.Flush()
    $fs.Position = $dataStart; $fs.CopyTo($out)
    foreach ($a in $append) { $out.Write($a.bytes, 0, $a.bytes.Length) }
    $out.Flush()
  } finally { $bw.Dispose(); $out.Dispose() }
} finally { $br.Dispose(); $fs.Dispose() }

# ---------------- read back & verify ----------------
$rf = [System.IO.File]::OpenRead($tmp); $rr = New-Object System.IO.BinaryReader($rf)
try {
  $v0 = $rr.ReadUInt32(); $v1 = $rr.ReadUInt32(); $v2 = $rr.ReadUInt32(); $v3 = $rr.ReadUInt32()
  $ds = 8 + [int64]$v1
  $vh = $Utf8.GetString($rr.ReadBytes([int]$v3)) | ConvertFrom-Json
  foreach ($a in $append) {
    $e = $vh.files.($a.name)
    $rf.Position = $ds + [int64]$e.offset
    $got = $rr.ReadBytes([int]$e.size)
    if ($got.Length -ne $a.bytes.Length -or (Get-Sha256Hex $got 0 $got.Length) -ne $e.integrity.hash -or
        (Get-Sha256Hex $a.bytes 0 $a.bytes.Length) -ne $e.integrity.hash) { throw "readback mismatch: $($a.name)" }
  }
  if ($rf.Length -ne $ds + ($vh.files.($append[$append.Count - 1].name).offset -as [int64]) + $append[$append.Count - 1].bytes.Length) { throw 'readback size mismatch' }
} finally { $rr.Dispose(); $rf.Dispose() }
Move-Item -LiteralPath $tmp -Destination $OutAsar -Force
$outHash = Get-FileSha256 $OutAsar
Write-Host "written: $OutAsar"
Write-Host "sha256 : $outHash"
if ($set.expectedOutputSha256 -and $outHash -ne [string]$set.expectedOutputSha256) {
  Remove-Item -LiteralPath $OutAsar -Force
  throw "output hash $outHash does not match the tested build $($set.expectedOutputSha256); refusing"
}
exit 0

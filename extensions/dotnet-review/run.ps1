#Requires -Version 7.0
<#
dotnet-review — build, test and analyze a .NET repo.
Emits a trailing fenced ```json block consumed by Firestarter's StatsExtractor.
Repo is already checked out at $env:FIRESTARTER_REPO by the runner.

Uses [Console]::Out.WriteLine for all output so it reliably reaches the
parent process's captured stdout regardless of PowerShell's host/streams.
#>

$ErrorActionPreference = 'Continue'
Set-StrictMode -Off

function Out-Line {
  param([string]$text = '')
  [Console]::Out.WriteLine($text)
}

function Out-Many {
  param($lines)
  if ($null -eq $lines) { return }
  foreach ($l in $lines) { [Console]::Out.WriteLine([string]$l) }
}

function Emit-Stats {
  param($obj)
  if ($null -eq $obj) {
    $json = '{"error":"Emit-Stats received null"}'
  } else {
    $json = $obj | ConvertTo-Json -Depth 12
    if ([string]::IsNullOrWhiteSpace($json) -or $json -eq 'null') {
      $json = '{"error":"ConvertTo-Json produced null for stats object","type":"' + $obj.GetType().FullName + '"}'
    }
  }
  Out-Line ''
  Out-Line '```json'
  Out-Line $json
  Out-Line '```'
}

$repo = $env:FIRESTARTER_REPO
if (-not $repo) { throw "FIRESTARTER_REPO env var missing" }
Set-Location $repo

# ---------------------------------------------------------------------------
# Target resolution: prefer .sln/.slnx at repo root, then recursive, then csproj
# ---------------------------------------------------------------------------
function Resolve-Target {
  foreach ($pat in @('*.sln', '*.slnx')) {
    $top = @(Get-ChildItem -Path $repo -Filter $pat -File -ErrorAction SilentlyContinue)
    if ($top.Count -gt 0) {
      $named = $top | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) -eq (Split-Path $repo -Leaf) }
      if ($named) { return $named[0].FullName }
      return ($top | Sort-Object Name | Select-Object -First 1).FullName
    }
  }
  foreach ($pat in @('*.sln', '*.slnx')) {
    $rec = @(Get-ChildItem -Path $repo -Filter $pat -File -Recurse -Depth 4 -ErrorAction SilentlyContinue)
    if ($rec.Count -gt 0) {
      return ($rec | Sort-Object FullName | Select-Object -First 1).FullName
    }
  }
  $proj = @(Get-ChildItem -Path $repo -Filter '*.csproj' -File -Recurse -Depth 5 -ErrorAction SilentlyContinue)
  if ($proj.Count -gt 0) { return ($proj | Sort-Object FullName | Select-Object -First 1).FullName }
  return $null
}

$target = Resolve-Target
if (-not $target) {
  Emit-Stats ([PSCustomObject]@{
    schema = 'dotnet-review/v1'
    error  = 'No .sln/.slnx/.csproj found in the repository'
    repo   = $repo
  })
  exit 1
}

Out-Line "Firestarter dotnet-review"
Out-Line "Repo   : $repo"
Out-Line "Target : $target"
Out-Line ''

# ---------------------------------------------------------------------------
# Restore
# ---------------------------------------------------------------------------
Out-Line '=== dotnet restore ==='
$restoreLines = & dotnet restore $target --nologo 2>&1
Out-Many $restoreLines
$restoreExit = $LASTEXITCODE

# ---------------------------------------------------------------------------
# Build
# - Visual Studio runs code-style (IDE####) analyzers during design-time/build;
#   `dotnet build` skips them unless EnforceCodeStyleInBuild and analyzers are on.
# - WarningLevel=999 surfaces the full compiler warning set for the toolchain.
# - v:normal keeps compiler diagnostics on stdout (v:minimal can omit some context).
# ---------------------------------------------------------------------------
Out-Line ''
Out-Line '=== dotnet build ==='
$buildLines = & dotnet build $target -c Release --no-restore --no-incremental --nologo -v:normal `
  -clp:NoSummary `
  /p:TreatWarningsAsErrors=false `
  /p:GenerateFullPaths=true `
  /p:RunAnalyzersDuringBuild=true `
  /p:EnableNETAnalyzers=true `
  /p:EnforceCodeStyleInBuild=true `
  /p:WarningLevel=999 2>&1
Out-Many $buildLines
$buildExit = $LASTEXITCODE

# MSBuild / dotnet-format diagnostic line format (often prefixed with "N>" for multiprocess logger):
#   [<N>>]<file>(<line>,<col>): <severity> <code>: <message> [<project>]
# `code` matches Roslyn-style codes (CA1822, IDE0051) and dotnet-format all-caps codes (WHITESPACE, FINALNEWLINE).
# `message` severity is normalized to info; `suggestion` (Roslyn) is kept as suggestion -> UI maps to info.
$diagRegex = '^\s*(?:\d+>\s*)?(?<file>[A-Za-z]:[^()]*?|[^()]+?)\((?<line>\d+)(?:,(?<col>\d+))?\)\s*:\s*(?<sev>warning|error|info|message|suggestion)\s+(?<code>[A-Z][A-Z0-9_]*|[A-Za-z]+[0-9]+)\s*:\s*(?<msg>.+?)(?:\s+\[(?<proj>[^\]]+)\])?\s*$'

$diagnostics = New-Object System.Collections.Generic.List[object]
function Add-DiagnosticsFromLines {
  param([object[]]$lines)
  if ($null -eq $lines) { return }
  foreach ($raw in $lines) {
    $text = [string]$raw
    if ($text -match $diagRegex) {
      $sev = $Matches.sev.ToLowerInvariant()
      if ($sev -eq 'message' -or $sev -eq 'suggestion') { $sev = 'info' }
      $diagnostics.Add([pscustomobject]@{
        file     = $Matches.file
        line     = [int]$Matches.line
        column   = if ($Matches.col) { [int]$Matches.col } else { 0 }
        severity = $sev
        code     = $Matches.code
        message  = $Matches.msg.Trim()
        project  = $Matches.proj
      })
    }
  }
}

Add-DiagnosticsFromLines $buildLines

# ---------------------------------------------------------------------------
# dotnet format — surfaces info-level (suggestion) diagnostics that the
# MSBuild console logger drops, e.g. WHITESPACE / FINALNEWLINE / IDE0055.
# --verify-no-changes makes it a non-mutating analysis pass; severity:info
# matches what VS shows live.
# ---------------------------------------------------------------------------
Out-Line ''
Out-Line '=== dotnet format --verify-no-changes --severity info ==='
$formatLines = & dotnet format $target --verify-no-changes --severity info --no-restore --verbosity normal 2>&1
Out-Many $formatLines
Add-DiagnosticsFromLines $formatLines

$diagnostics = @($diagnostics | Sort-Object file, line, column, code, message -Unique)

$diagErrors   = @($diagnostics | Where-Object { $_.severity -eq 'error' })
$diagWarnings = @($diagnostics | Where-Object { $_.severity -eq 'warning' })
$diagInfos    = @($diagnostics | Where-Object { $_.severity -eq 'info' })

$warnByCode = @($diagnostics | Group-Object code | Sort-Object Count -Descending | ForEach-Object {
  [pscustomobject]@{ code = $_.Name; count = $_.Count }
})

Out-Line ''
Out-Line ("Diagnostics: {0} errors, {1} warnings, {2} info" -f $diagErrors.Count, $diagWarnings.Count, $diagInfos.Count)

# ---------------------------------------------------------------------------
# Tests (skip cleanly when build failed)
# ---------------------------------------------------------------------------
$testTotal    = 0
$testPassed   = 0
$testFailed   = 0
$testSkipped  = 0
$testDuration = 0
$testRan      = $false
$testExitCode = $null
$testFailures = New-Object System.Collections.Generic.List[object]

if ($buildExit -eq 0) {
  Out-Line ''
  Out-Line '=== dotnet test ==='
  $trxDir = Join-Path ([IO.Path]::GetTempPath()) "firestarter-review-$([guid]::NewGuid().ToString('N'))"
  New-Item -ItemType Directory -Force -Path $trxDir | Out-Null
  $testLines = & dotnet test $target -c Release --no-build --nologo `
    --logger "trx;LogFileName=results.trx" `
    --results-directory $trxDir 2>&1
  Out-Many $testLines
  $testExitCode = $LASTEXITCODE
  $testRan = $true

  $trxFiles = @(Get-ChildItem -Path $trxDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
  foreach ($trx in $trxFiles) {
    try {
      [xml]$xml = Get-Content -LiteralPath $trx.FullName -Raw
      $counters = $xml.TestRun.ResultSummary.Counters
      if ($counters) {
        if ($counters.total)       { $testTotal   += [int]$counters.total }
        if ($counters.passed)      { $testPassed  += [int]$counters.passed }
        if ($counters.failed)      { $testFailed  += [int]$counters.failed }
        if ($counters.notExecuted) { $testSkipped += [int]$counters.notExecuted }
      }
      $times = $xml.TestRun.Times
      if ($times -and $times.start -and $times.finish) {
        try {
          $span = [datetime]$times.finish - [datetime]$times.start
          $testDuration += [int]$span.TotalMilliseconds
        } catch {}
      }
      foreach ($r in @($xml.TestRun.Results.UnitTestResult)) {
        if ($r -and $r.outcome -and $r.outcome -ne 'Passed') {
          $msg = $null
          try { $msg = $r.Output.ErrorInfo.Message } catch {}
          $testFailures.Add([PSCustomObject]@{
            name     = [string]$r.testName
            outcome  = [string]$r.outcome
            duration = [string]$r.duration
            message  = ([string]$msg).Trim()
          })
        }
      }
    } catch {
      Out-Line "Failed to parse $($trx.FullName): $_"
    }
  }

  try { Remove-Item -Recurse -Force $trxDir -ErrorAction SilentlyContinue } catch {}
} else {
  Out-Line ''
  Out-Line 'Skipping tests: build failed.'
}

# ---------------------------------------------------------------------------
# Outdated packages
# ---------------------------------------------------------------------------
Out-Line ''
Out-Line '=== dotnet list package --outdated ==='
$outdated = New-Object System.Collections.Generic.List[object]
$outdatedError = $null
try {
  $outdatedLines = & dotnet list $target package --outdated --format json 2>&1
  Out-Many $outdatedLines
  $raw = ($outdatedLines | Out-String)
  $startIdx = $raw.IndexOf('{')
  $endIdx   = $raw.LastIndexOf('}')
  if ($startIdx -ge 0 -and $endIdx -gt $startIdx) {
    $json = $raw.Substring($startIdx, $endIdx - $startIdx + 1)
    $parsed = $json | ConvertFrom-Json
    foreach ($proj in @($parsed.projects)) {
      if ($null -eq $proj) { continue }
      $projName = if ($proj.path) { Split-Path $proj.path -Leaf } else { '' }
      $frameworks = $proj.frameworks
      if ($null -eq $frameworks) { continue }
      foreach ($fw in $frameworks) {
        if ($null -eq $fw) { continue }
        $pkgs = $fw.topLevelPackages
        if ($null -eq $pkgs) { continue }
        foreach ($pkg in $pkgs) {
          if ($null -eq $pkg) { continue }
          $outdated.Add([PSCustomObject]@{
            project   = $projName
            framework = [string]$fw.framework
            id        = [string]$pkg.id
            requested = [string]$pkg.requestedVersion
            current   = [string]$pkg.resolvedVersion
            latest    = [string]$pkg.latestVersion
          })
        }
      }
    }
  } else {
    $outdatedError = 'No JSON payload in output'
  }
} catch {
  $outdatedError = $_.Exception.Message
}
Out-Line ("Outdated packages: {0}" -f $outdated.Count)

# ---------------------------------------------------------------------------
# Emit stats
# ---------------------------------------------------------------------------
# Convert List<object> to plain arrays BEFORE stuffing into hashtables.
# PowerShell 7 throws "Argument types do not match" when wrapping a generic
# List in @() inside a hashtable literal. ToArray() avoids the coercion bug.
$outdatedArr     = $outdated.ToArray()
$testFailuresArr = $testFailures.ToArray()
$diagErrorsArr   = @($diagErrors)
$diagWarningsArr = @($diagWarnings)
$diagInfosArr    = @($diagInfos)
$warnByCodeArr   = @($warnByCode)

$summary = [PSCustomObject]@{
  buildSucceeded   = ($buildExit -eq 0)
  testsPassed      = ($testRan -and $testFailed -eq 0 -and $testTotal -gt 0)
  analyzerErrors   = [int]$diagErrorsArr.Count
  analyzerWarnings = [int]$diagWarningsArr.Count
  outdatedCount    = [int]$outdatedArr.Count
}

$tests = [PSCustomObject]@{
  ran        = [bool]$testRan
  exitCode   = $testExitCode
  total      = [int]$testTotal
  passed     = [int]$testPassed
  failed     = [int]$testFailed
  skipped    = [int]$testSkipped
  durationMs = [int]$testDuration
  failures   = $testFailuresArr
}

$build = [PSCustomObject]@{
  restoreExit = [int]$restoreExit
  exitCode    = [int]$buildExit
  succeeded   = ($buildExit -eq 0)
  errors      = [int]$diagErrorsArr.Count
  warnings    = [int]$diagWarningsArr.Count
  info        = [int]$diagInfosArr.Count
}

$outdatedObj = [PSCustomObject]@{
  count    = [int]$outdatedArr.Count
  error    = $outdatedError
  packages = $outdatedArr
}

$analyzers = [PSCustomObject]@{
  errors   = $diagErrorsArr
  warnings = $diagWarningsArr
  info     = $diagInfosArr
  byCode   = $warnByCodeArr
}

$stats = [PSCustomObject]@{
  schema    = 'dotnet-review/v1'
  target    = [string]$target
  summary   = $summary
  build     = $build
  tests     = $tests
  outdated  = $outdatedObj
  analyzers = $analyzers
}

Emit-Stats $stats

if ($buildExit -ne 0) { exit $buildExit }
if ($testRan -and $testExitCode -ne 0) { exit $testExitCode }
exit 0

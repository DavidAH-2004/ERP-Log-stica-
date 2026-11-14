#requires -Version 5.1
<#
F315: Vercel Production auto-promotion policy guard.
Safe default: read-only. Does not promote deployments, mutate GitHub, Render or Neon,
or print the authenticated API JSON. RestorePolicy requires a deliberate phrase.
Usage:
  powershell -NoProfile -File .\scripts\f315-vercel-promotion-policy.ps1
  powershell -NoProfile -File .\scripts\f315-vercel-promotion-policy.ps1 -Mode RestorePolicy -Approval F315_RESTORE_MANUAL_PROMOTION
Verify BEFORE each production deployment and immediately AFTER any Vercel promotion:
https://github.com/vercel/vercel/issues/15095 (OPEN on 2026-11-14).
#>
[CmdletBinding()]
param(
    [ValidateSet('Check', 'RestorePolicy')][string]$Mode = 'Check',
    [string]$Approval = ''
)
$ErrorActionPreference = 'Stop'
$project = 'prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82'
$team = 'team_pVnk0dDHN0HvfQEqg5IwS1DF'
$endpoint = '/v9/projects/{0}?teamId={1}' -f $project, $team
$approvalPhrase = 'F315_RESTORE_MANUAL_PROMOTION'

# A real authenticated Vercel CLI must already be installed by the operator.
# Never use --verbose, dump API JSON, or pass tokens as command arguments.
if (-not (Get-Command vercel -ErrorAction SilentlyContinue)) {
    throw 'F315_BLOCKED: Vercel CLI is not available.'
}
if ($Mode -eq 'RestorePolicy' -and $Approval -cne $approvalPhrase) {
    throw 'F315_BLOCKED: explicit recovery authorization phrase missing.'
}

function Invoke-F315VercelProject {
    param([ValidateSet('GET','PATCH')][string]$Method)
    $params = @('api', $endpoint, '--method', $Method, '--raw')
    if ($Method -eq 'PATCH') {
        # The only property written by this command is autoAssignCustomDomains=false.
        $params += @('--field', 'autoAssignCustomDomains=false')
    }
    $raw = @(& vercel @params 2>$null)
    if ($LASTEXITCODE -ne 0) { throw ('F315_BLOCKED: authenticated Vercel ' + $Method + ' failed.') }
    try { $result = (($raw -join [Environment]::NewLine) | ConvertFrom-Json -ErrorAction Stop) }
    catch { throw 'F315_BLOCKED: Vercel returned invalid JSON.' }
    if (-not $result -or $result.id -cne $project) {
        throw 'F315_BLOCKED: unexpected Vercel project identifier.'
    }
    return $result
}
function Get-F315Policy {
    $p = Invoke-F315VercelProject -Method GET
    $field = $p.PSObject.Properties['autoAssignCustomDomains']
    if ($null -eq $field -or $field.Value -isnot [bool]) {
        throw 'F315_BLOCKED: autoAssignCustomDomains is unavailable or not boolean.'
    }
    return [bool]$field.Value
}

$current = Get-F315Policy
if ($Mode -eq 'Check') {
    if ($current) { throw 'F315_NO_GO: automatic Vercel production domain assignment is ENABLED.' }
    Write-Output ('F315_POLICY_PASS=manual_promotion_only; project=' + $project)
    return
}
if (-not $current) {
    Write-Output 'F315_POLICY_PASS=already_safe; no_write_performed'
    return
}
# An explicitly approved repair. Do not run this in regular CI.
$null = Invoke-F315VercelProject -Method PATCH
if (Get-F315Policy) { throw 'F315_NO_GO: repair was not confirmed; automatic promotion remains enabled.' }
Write-Output 'F315_POLICY_RESTORED=manual_promotion_only; verified_by_followup_GET'

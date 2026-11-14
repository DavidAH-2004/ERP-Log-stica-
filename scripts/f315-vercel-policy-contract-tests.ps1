#requires -Version 5.1
# F315: test the actual production-policy PowerShell script without Vercel credentials/network.
$ErrorActionPreference = 'Stop'
$guard = Join-Path $PSScriptRoot 'f315-vercel-promotion-policy.ps1'
if (-not (Test-Path -LiteralPath $guard)) { throw 'F315 script missing.' }
$global:F315MockValue = $false
$global:F315MockProject = 'prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82'
$global:F315MockMissing = $false
$global:F315MockInvalidJson = $false
$global:F315MockGetFail = $false
$global:F315MockPatchFail = $false
$global:F315MockKeepTrue = $false
$global:F315MockPatchCalls = 0
$global:F315MockGetCalls = 0

function global:vercel {
    $parts = @($args)
    $methodIndex = [array]::IndexOf($parts, '--method')
    if ($parts[0] -ne 'api' -or $methodIndex -lt 0 -or $parts[1] -notmatch '^/v9/projects/prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82\?teamId=team_pVnk0dDHN0HvfQEqg5IwS1DF$') {
        $global:LASTEXITCODE = 80
        return ''
    }
    $method = $parts[$methodIndex + 1]
    if ($method -eq 'PATCH') {
        $global:F315MockPatchCalls++
        if ($global:F315MockPatchFail -or
            ([array]::IndexOf($parts, '--field') -lt 0) -or
            ([array]::IndexOf($parts, 'autoAssignCustomDomains=false') -lt 0)) {
            $global:LASTEXITCODE = 81
            return ''
        }
        if (-not $global:F315MockKeepTrue) { $global:F315MockValue = $false }
    }
    elseif ($method -eq 'GET') {
        $global:F315MockGetCalls++
        if ($global:F315MockGetFail) { $global:LASTEXITCODE = 82; return '' }
    }
    else { $global:LASTEXITCODE = 83; return '' }
    $global:LASTEXITCODE = 0
    if ($global:F315MockInvalidJson) { return '!not-json!' }
    $payload = @{ id = $global:F315MockProject; autoAssignCustomDomains = $global:F315MockValue }
    if ($global:F315MockMissing) { $payload.Remove('autoAssignCustomDomains') }
    return ($payload | ConvertTo-Json -Compress)
}

function Reset-F315Mock {
    $global:F315MockValue = $false
    $global:F315MockProject = 'prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82'
    $global:F315MockMissing = $false
    $global:F315MockInvalidJson = $false
    $global:F315MockGetFail = $false
    $global:F315MockPatchFail = $false
    $global:F315MockKeepTrue = $false
    $global:F315MockPatchCalls = 0
    $global:F315MockGetCalls = 0
}
$count = 0
function Assert-F315 {
    param([string]$Label, [bool]$MustFail, [int]$ExpectedPatches = 0, [string]$Mode = 'Check', [string]$Approval = '')
    $failed = $false
    $msg = ''
    try { $msg = ((& $guard -Mode $Mode -Approval $Approval) -join [Environment]::NewLine) }
    catch { $failed = $true; $msg = $_.Exception.Message }
    if ($failed -ne $MustFail) { throw ('F315 TEST FAIL [' + $Label + '] result=' + $msg) }
    if ($global:F315MockPatchCalls -ne $ExpectedPatches) {
        throw ('F315 TEST FAIL [' + $Label + '] patch count=' + $global:F315MockPatchCalls)
    }
    $script:count++
    Write-Output ('PASS ' + $Label)
}
try {
    Reset-F315Mock
    Assert-F315 -Label 'safe policy read-only' -MustFail $false
    Reset-F315Mock; $global:F315MockValue = $true
    Assert-F315 -Label 'auto promotion true blocks' -MustFail $true
    Reset-F315Mock; $global:F315MockProject = 'prj_wrong'
    Assert-F315 -Label 'wrong project ID blocks' -MustFail $true
    Reset-F315Mock; $global:F315MockMissing = $true
    Assert-F315 -Label 'missing flag blocks' -MustFail $true
    Reset-F315Mock; $global:F315MockValue = $true
    Assert-F315 -Label 'repair without approval blocks' -MustFail $true -Mode RestorePolicy
    Reset-F315Mock; $global:F315MockValue = $true
    Assert-F315 -Label 'repair with exact approval succeeds' -MustFail $false -ExpectedPatches 1 -Mode RestorePolicy -Approval F315_RESTORE_MANUAL_PROMOTION
    Reset-F315Mock
    Assert-F315 -Label 'already safe causes no write' -MustFail $false -Mode RestorePolicy -Approval F315_RESTORE_MANUAL_PROMOTION
    Reset-F315Mock; $global:F315MockValue = $true; $global:F315MockPatchFail = $true
    Assert-F315 -Label 'PATCH failure blocks' -MustFail $true -ExpectedPatches 1 -Mode RestorePolicy -Approval F315_RESTORE_MANUAL_PROMOTION
    Reset-F315Mock; $global:F315MockValue = $true; $global:F315MockKeepTrue = $true
    Assert-F315 -Label 'repair verification mismatch blocks' -MustFail $true -ExpectedPatches 1 -Mode RestorePolicy -Approval F315_RESTORE_MANUAL_PROMOTION
    Reset-F315Mock; $global:F315MockGetFail = $true
    Assert-F315 -Label 'GET failure blocks' -MustFail $true
    Reset-F315Mock; $global:F315MockInvalidJson = $true
    Assert-F315 -Label 'invalid JSON blocks' -MustFail $true
    Write-Output ('F315_PASS=' + $count + '_OFFLINE_SCENARIOS')
}
finally {
    Remove-Item Function:\vercel -ErrorAction SilentlyContinue
}

# F424 - QA Admin read-only against protected Vercel Preview + Render DEV.
# Do not log passwords, JWT, EANs or business data. No Production / business writes.
[CmdletBinding()]
param(
    [ValidatePattern('^dpl_[A-Za-z0-9]+$')]
    [string]$DeploymentId = 'dpl_7qLQtfePvPMqjmP8aQWRmkVnAdEz'
)
$ErrorActionPreference='Stop'
$render='https://marketplaceerp-api-staging.onrender.com'
$cli=Join-Path $env:APPDATA 'npm\vercel.cmd'
if(!(Test-Path -LiteralPath $cli)){throw 'F424_VERCEL_CLI_MISSING'}
$tmp=Join-Path $env:TEMP ('F424_admin_'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path (Join-Path $tmp '.vercel') -Force
@{orgId='team_pVnk0dDHN0HvfQEqg5IwS1DF';projectId='prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82'} |
 ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $tmp '.vercel\project.json') -Encoding Ascii
$secure=$null;$password=$null;$token=$null;$payload=$null;$loginRaw=$null;$login=$null

function PreviewGet([string]$path,[string]$jwt){
 if($path -cne '/api/productos/etiquetas' -and $path -cne '/api/inventario/conteos-ciclicos/sesiones' -and $path -notmatch '^/api/productos/por-codigo/[0-9]{13}$'){throw 'F424_ROUTE_DENIED'}
 $headerLine='Authorization: Bearer '+$jwt+[Environment]::NewLine
 $raw=($headerLine | & $cli curl $path --deployment $DeploymentId -- --silent --show-error --max-redirs 0 --request GET --header '@-' --write-out 'F424_HTTP=%{http_code}' 2>$null | Out-String)
 if($LASTEXITCODE -ne 0 -or $raw -notmatch 'F424_HTTP=(\d{3})'){throw 'F424_PREVIEW_UNAVAILABLE'}
 return [pscustomobject]@{status=[int]$Matches[1];content=($raw -split 'F424_HTTP=',2)[0].Trim()}
}
function RenderGet([string]$path,[string]$jwt){
 if($path -cne '/api/productos/etiquetas' -and $path -cne '/api/inventario/conteos-ciclicos/sesiones' -and $path -notmatch '^/api/productos/por-codigo/[0-9]{13}$'){throw 'F424_ROUTE_DENIED'}
 $uri=[uri]($render+$path)
 if($uri.Scheme -cne 'https' -or $uri.Host -cne 'marketplaceerp-api-staging.onrender.com'){throw 'F424_NOT_DEV'}
 try {
  $r=Invoke-WebRequest -Uri $uri -Method GET -Headers @{Authorization=('Bearer '+$jwt);Accept='application/json'} -UseBasicParsing -TimeoutSec 45 -MaximumRedirection 0 -ErrorAction Stop
  return [pscustomobject]@{status=[int]$r.StatusCode;content=$r.Content}
 }catch{
  if($_.Exception.Response -and $_.Exception.Response.StatusCode){return [pscustomobject]@{status=[int]$_.Exception.Response.StatusCode;content=''}}
  throw 'F424_RENDER_UNAVAILABLE'
 }
}
function Canon($items){
 return (@($items | ForEach-Object {([string]$_.id+'|'+[string]$_.sku+'|'+[string]$_.codigoEAN)} | Sort-Object) | ConvertTo-Json -Compress)
}
try {
 Push-Location $tmp
 try {
  $secure=Read-Host 'Contrasena Admin STAGING (oculta)' -AsSecureString
  if($null -eq $secure -or $secure.Length -eq 0){throw 'F424_PASSWORD_EMPTY'}
  $password=[System.Net.NetworkCredential]::new('',$secure).Password
  $payload=@{email='admin.evaluacion@erp.example';password=$password}|ConvertTo-Json -Compress
  $loginRaw=($payload | & $cli curl /api/auth/login --deployment $DeploymentId -- --silent --show-error --max-redirs 0 --request POST --header 'Content-Type: application/json' --data-binary '@-' --write-out 'F424_HTTP=%{http_code}' 2>$null | Out-String)
  $password=$null;$payload=$null
  if($LASTEXITCODE -ne 0 -or $loginRaw -notmatch 'F424_HTTP=(\d{3})' -or [int]$Matches[1] -ne 200){throw 'F424_LOGIN_NOT_200'}
  $login=(($loginRaw -split 'F424_HTTP=',2)[0].Trim())|ConvertFrom-Json
  $loginRaw=$null
  if([string]$login.role -cne 'Admin' -or [string]::IsNullOrWhiteSpace($login.token)){throw 'F424_ADMIN_ROLE_INVALID'}
  $token=[string]$login.token
  Write-Output 'F424_ADMIN_PREVIEW_LOGIN=200'
  $labelsA=PreviewGet '/api/productos/etiquetas' $token
  $labelsB=RenderGet '/api/productos/etiquetas' $token
  if($labelsA.status -ne 200 -or $labelsB.status -ne 200){throw 'F424_LABELS_NOT_200'}
  $a=@($labelsA.content|ConvertFrom-Json|ForEach-Object {$_})
  $b=@($labelsB.content|ConvertFrom-Json|ForEach-Object {$_})
  if($a.Count -lt 1 -or $a.Count -ne $b.Count -or (Canon $a) -cne (Canon $b)){throw 'F424_LABELS_MISMATCH'}
  $selected=$a|Where-Object {[string]$_.codigoEAN -match '^[0-9]{13}$'}|Select-Object -First 1
  if($null -eq $selected){throw 'F424_NO_VALID_BARCODE'}
  $lookup='/api/productos/por-codigo/'+[string]$selected.codigoEAN
  $lookA=PreviewGet $lookup $token;$lookB=RenderGet $lookup $token
  if($lookA.status -ne 200 -or $lookB.status -ne 200){throw 'F424_LOOKUP_NOT_200'}
  $foundA=$lookA.content|ConvertFrom-Json;$foundB=$lookB.content|ConvertFrom-Json
  if([string]$foundA.id -cne [string]$selected.id -or [string]$foundB.id -cne [string]$selected.id -or [string]$foundA.sku -cne [string]$foundB.sku){throw 'F424_LOOKUP_MISMATCH'}
  $sA=PreviewGet '/api/inventario/conteos-ciclicos/sesiones' $token
  $sB=RenderGet '/api/inventario/conteos-ciclicos/sesiones' $token
  if($sA.status -ne 200 -or $sB.status -ne 200){throw 'F424_SESSION_NOT_200'}
  $sessionsA=@($sA.content|ConvertFrom-Json|ForEach-Object {$_})
  $sessionsB=@($sB.content|ConvertFrom-Json|ForEach-Object {$_})
  if($sessionsA.Count -ne $sessionsB.Count){throw 'F424_SESSION_COUNT_MISMATCH'}
  Write-Output ('F424_ADMIN_LABELS='+$a.Count)
  Write-Output ('F424_ADMIN_SESSIONS='+$sessionsA.Count)
  Write-Output 'F424_ADMIN_PREVIEW_RENDER_READONLY_PASS'
  Write-Output 'F424_BUSINESS_WRITES=0'
 }finally {Pop-Location}
}finally {
 $password=$null;$secure=$null;$token=$null;$loginRaw=$null;$login=$null;$payload=$null
 Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
 Write-Output 'F424_TEMP_CLEANED'
}

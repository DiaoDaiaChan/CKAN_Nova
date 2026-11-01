$here = $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $here)
Write-Output ("HERE=[" + $here + "] ROOT=[" + $root + "]")
$solution = Join-Path $root 'ckan-ui.sln'
Write-Output ("SOLUTION=[" + $solution + "] EXISTS=" + (Test-Path $solution))
$logDir = Join-Path $root 'artifacts/tmp'
Write-Output ("LOGDIR=[" + $logDir + "] EXISTS=" + (Test-Path $logDir))
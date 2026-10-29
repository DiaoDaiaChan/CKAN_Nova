#Requires -Version 5.1
<#
.SYNOPSIS
    生成一张自签名代码签名证书并导出 .pfx，供本机测试 Sign-Artifacts.ps1 用。

.DESCRIPTION
    用途只有一个：在没有公司证书、没有 EV/OV 证书的开发机上，把「签名 → 验签」这条链路
    跑通。**自签证书不能用于对外发布**：它不在任何用户的受信任根里，签出来的包在别人机器上
    仍然是「未知发布者」，只有本机导入受信任根之后才验成 Valid。

    流程：
      1) 在 Cert:\CurrentUser\My 生成 CodeSigningCert（RSA 3072 / SHA256，默认 1 年）
      2) 导出 <OutDir>\dev-code-signing.pfx（带口令）与 dev-code-signing.cer（公钥）
      3) 打印：签名命令、本机信任命令、清除命令
      4) 除非给 -Keep，否则把刚生成的证书从证书存储里删掉（.pfx/.cer 文件保留）

    证书存储是「每用户」的，不需要管理员权限。

.PARAMETER Subject
    证书主题，默认 'CN=CKAN UI Dev Code Signing (UNTRUSTED)'。
    故意写得难看：别让人误以为这是发布证书。

.PARAMETER OutDir
    pfx/cer 输出目录，默认 <repo>\artifacts\certs。

.PARAMETER PfxPassword
    pfx 口令，默认 'ckanui-dev'。仅本机测试用；对外签名的 pfx 口令要走 secret 管理。

.PARAMETER Years
    有效期年数，默认 1。

.PARAMETER Trust
    顺手把公钥证书导入 Cert:\CurrentUser\Root，让本机验签变 Valid。
    只对本机有效，且这是真实的安全边界变更：装了就不再提示未知发布者。默认不做。

.PARAMETER Keep
    把证书留在 Cert:\CurrentUser\My（默认会删掉，只留 pfx/cer 文件）。

.EXAMPLE
    powershell -NoProfile -File tools\release\New-DevCertificate.ps1

.EXAMPLE
    # 生成 + 导入本机受信任根 + 保留证书，然后试签
    powershell -NoProfile -File tools\release\New-DevCertificate.ps1 -Trust -Keep
    powershell -NoProfile -File tools\release\Sign-Artifacts.ps1 -Path .\x.exe -PfxPath .\artifacts\certs\dev-code-signing.pfx -PfxPassword ckanui-dev
#>
[CmdletBinding()]
param(
    [string] $Subject = 'CN=CKAN UI Dev Code Signing (UNTRUSTED)',

    [string] $OutDir = '',

    [string] $PfxPassword = 'ckanui-dev',

    [int] $Years = 1,

    [switch] $Trust,

    [switch] $Keep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Write-Info {
    param([string] $Message)
    Write-Host ('    ' + $Message)
}

function Write-Note {
    param([string] $Message)
    Write-Host ('    注意：' + $Message) -ForegroundColor Yellow
}

function Exit-WithError {
    param([string] $Message, [int] $Code = 1)
    Write-Host ''
    Write-Host ('失败：' + $Message) -ForegroundColor Red
    exit $Code
}

# 步骤号用计数器，这样可选步骤（-Trust）跳过后编号仍然连续。
$script:StepNo = 0

function Write-Step {
    param([string] $Message)
    $script:StepNo++
    Write-Host ''
    Write-Host ('[{0}] {1}' -f $script:StepNo, $Message) -ForegroundColor Cyan
}

# ---- 1. 环境检查 ------------------------------------------------------------

Write-Step '检查环境'

$cmd = Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue
if (-not $cmd) {
    Exit-WithError '当前 PowerShell 没有 New-SelfSignedCertificate（需要 Windows PowerShell 5.1 或带 PKI 模块的 PowerShell 7）。'
}
Write-Info ('PowerShell ' + $PSVersionTable.PSVersion.ToString() + '，New-SelfSignedCertificate 可用')

if ($OutDir -eq '') {
    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $OutDir = Join-Path $repoRoot 'artifacts\certs'
}
if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
Write-Info ('输出目录 ' + $OutDir)

if ($PfxPassword -eq '') {
    Exit-WithError '-PfxPassword 不能为空：导出 pfx 必须有口令。'
}

$pfxPath = Join-Path $OutDir 'dev-code-signing.pfx'
$cerPath = Join-Path $OutDir 'dev-code-signing.cer'

# ---- 2. 生成证书 ------------------------------------------------------------

Write-Step '生成自签名代码签名证书'
Write-Info ('主题 ' + $Subject)
Write-Info ('有效期 ' + $Years + ' 年，RSA 3072 / SHA256，仅用于本机测试')

$notAfter = (Get-Date).AddYears($Years)
$certParams = @{
    Type              = 'CodeSigningCert'
    Subject           = $Subject
    KeyUsage          = 'DigitalSignature'
    KeyAlgorithm      = 'RSA'
    KeyLength         = 3072
    HashAlgorithm     = 'SHA256'
    KeyExportPolicy   = 'Exportable'
    CertStoreLocation = 'Cert:\CurrentUser\My'
    NotAfter          = $notAfter
}
$cert = New-SelfSignedCertificate @certParams

if (-not $cert) {
    Exit-WithError 'New-SelfSignedCertificate 没有返回证书。'
}
$thumbprint = $cert.Thumbprint
Write-Info ('指纹 ' + $thumbprint)
Write-Info ('主体 ' + $cert.Subject)

# ---- 3. 导出 pfx / cer ------------------------------------------------------

Write-Step '导出证书文件'

$certPath = 'Cert:\CurrentUser\My\' + $thumbprint
$secure = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
try {
    Export-PfxCertificate -Cert $certPath -FilePath $pfxPath -Password $secure -Force | Out-Null
}
catch {
    Exit-WithError ('导出 pfx 失败：' + $_.Exception.Message)
}
Export-Certificate -Cert $certPath -FilePath $cerPath -Force | Out-Null
Write-Info ('pfx ' + $pfxPath)
Write-Info ('cer ' + $cerPath)

# ---- 4. 可选：导入本机受信任根 ----------------------------------------------

$trustedRoot = $false
if ($Trust) {
    Write-Step '导入 Cert:\CurrentUser\Root（仅本机受信任）'
    try {
        Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
        $trustedRoot = $true
        Write-Info ('已导入 ' + $thumbprint + ' 到当前用户受信任根')
        Write-Note '这会让本机把该证书签的任何东西都当作受信任发布者。仅限本机测试，测完按下面命令删。'
    }
    catch {
        Write-Note ('导入受信任根失败：' + $_.Exception.Message)
    }
}

# ---- 5. 打印用法与清除命令 --------------------------------------------------

Write-Step '怎么用这张证书'
Write-Host ''
Write-Host '  # 签名（先 -DryRun 看命令，再真签）'
Write-Host ('  powershell -NoProfile -File tools\release\Sign-Artifacts.ps1 -Path .\你的文件.exe -PfxPath "' + $pfxPath + '" -PfxPassword "' + $PfxPassword + '" -DryRun')
Write-Host ''
Write-Host '  # 让本机把这张证书当成受信任发布者（仅本机测试；-Trust 做过就跳过）'
Write-Host ('  Import-Certificate -FilePath "' + $cerPath + '" -CertStoreLocation Cert:\CurrentUser\Root')
Write-Host ''
Write-Host '  # 清除：证书存储 + 受信任根 + 文件'
Write-Host ('  Remove-Item -LiteralPath "' + $certPath + '" -Force')
Write-Host ('  Remove-Item -LiteralPath "Cert:\CurrentUser\Root\' + $thumbprint + '" -Force -ErrorAction SilentlyContinue')
Write-Host ('  Remove-Item -LiteralPath "' + $pfxPath + '","' + $cerPath + '" -Force')
Write-Host ''
Write-Note '自签证书签出来的包在别人机器上仍然显示「未知发布者」；对外发布必须用 EV/OV 证书或 SignPath 之类的服务。'

# ---- 6. 清理证书存储 --------------------------------------------------------

Write-Step '清理'
if ($Keep) {
    Write-Info ('按 -Keep 保留证书：' + $certPath)
}
else {
    Remove-Item -LiteralPath $certPath -Force -ErrorAction SilentlyContinue
    Write-Info ('已从 Cert:\CurrentUser\My 删除 ' + $thumbprint + '（pfx/cer 文件保留）')
}
if ($trustedRoot) {
    Write-Info '受信任根里的那份保留着（由 -Trust 显式要求）；要删用上面第 5 步第二条命令。'
}

Write-Host ''
Write-Host '完成：自签代码签名证书已生成。' -ForegroundColor Green
exit 0

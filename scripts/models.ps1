# 下载并校验 STTmini 所需的模型文件（AGENTS.md §9.3）。
# 本脚本是 scripts/models.sh 的 Windows 原生等价物（PowerShell），不依赖 bash。
#
# 用法：
#   pwsh scripts/models.ps1                              # 下载到 ./models
#   pwsh scripts/models.ps1 -TargetDir .\.models-cache   # 指定目录
#   pwsh scripts/models.ps1 -Mirror https://hf-mirror.com  # 用镜像源
#   $env:STTMINI_MIRROR = 'https://hf-mirror.com'; pwsh scripts/models.ps1
#
# 校验：每个文件按 SHA256 验证（见下方表）。
# 手动放置：若网络受限，可手工下载文件放入目标目录，本脚本会跳过已存在文件（仅校验）。
#
# 国内网络提示：HuggingFace 直连可能被重置，推荐用镜像：
#   $env:STTMINI_MIRROR = 'https://hf-mirror.com'

[CmdletBinding()]
param(
    [string]$TargetDir = '.\models',
    [string]$Mirror = $env:STTMINI_MIRROR
)

$ErrorActionPreference = 'Stop'
# Invoke-WebRequest 在 PS 5.1 下用 IE 引擎解析会极慢/出错；PS 7 已移除该引擎。
# 下载期间统一关闭进度（大文件进度条会让 IWR 慢 10 倍以上）。
$global:ProgressPreference = 'SilentlyContinue'

# HuggingFace 可替换镜像前缀（Mirror 为空时回退官方源）。
$HfPrefix = if ([string]::IsNullOrEmpty($Mirror)) { 'https://huggingface.co' } else { $Mirror }
$HfRepo = 'csukuangfj/sherpa-onnx-paraformer-zh-2023-09-14/resolve/main'
$VadUrl = 'https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx'

# 文件 → 下载 URL。
$Urls = [ordered]@{
    'model.int8.onnx' = "$HfPrefix/$HfRepo/model.int8.onnx"
    'tokens.txt'      = "$HfPrefix/$HfRepo/tokens.txt"
    'am.mvn'          = "$HfPrefix/$HfRepo/am.mvn"
    'silero_vad.onnx' = $VadUrl
}

# 文件 → SHA256（实测值，来自 huggingface.co/csukuangfj 与 GitHub asr-models release）。
$Sha256 = @{
    'model.int8.onnx' = 'f36a0433bcf096bd6d6f11b80a3ac8bed110bdca632fe0d731df8d1a84475945'
    'tokens.txt'      = '59aba8873a2ed1e122c25fee421e25f283b63290efbde85c1f01a853d83cb6e6'
    'am.mvn'          = '29b3c740a2c0cfc6b308126d31d7f265fa2be74f3bb095cd2f143ea970896ae5'
    'silero_vad.onnx' = '9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6'
}

function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLower()
}

function Test-ModelFile {
    # 返回：$true = 已存在且校验通过；$false = 需下载
    param([string]$Name)
    $dest = Join-Path $TargetDir $Name
    if (-not (Test-Path -LiteralPath $dest -PathType Leaf)) { return $false }
    $expected = $Sha256[$Name]
    if ([string]::IsNullOrEmpty($expected)) { return $true } # 无校验值，存在即通过
    return (Get-FileSha256 $dest) -eq $expected.ToLower()
}

function Invoke-DownloadFile {
    # PS 5.1 的 IWR 默认走 IE 引擎，需 -UseBasicParsing；PS 7 已移除该参数。
    # 兼容两版：PSVersion 判断后再调用，避免传不识别的参数。
    param([string]$Url, [string]$Dest)
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        Invoke-WebRequest -Uri $Url -OutFile $Dest
    } else {
        Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing
    }
}

if (-not [string]::IsNullOrEmpty($Mirror)) { Write-Host "==> 使用镜像源：$Mirror" }
if (-not (Test-Path -LiteralPath $TargetDir)) { New-Item -ItemType Directory -Path $TargetDir | Out-Null }
$TargetDir = (Resolve-Path -LiteralPath $TargetDir).Path
Write-Host "==> 目标目录：$TargetDir"

foreach ($name in $Urls.Keys) {
    $dest = Join-Path $TargetDir $name
    $url = $Urls[$name]

    if (Test-ModelFile -Name $name) {
        Write-Host "[skip] $name 已存在且校验通过"
        continue
    }

    Write-Host "[download] $name <- $url"
    try {
        Invoke-DownloadFile -Url $url -Dest $dest
    } catch {
        Write-Host "[error] 下载失败：$name" -ForegroundColor Red
        Write-Host "        若网络受限，可用浏览器下载该文件放入 $TargetDir\ 后重跑本脚本。" -ForegroundColor Red
        throw
    }

    $expected = $Sha256[$name]
    if ([string]::IsNullOrEmpty($expected)) {
        Write-Host "[info] $name 已下载（未配置 SHA256）。实际值如下，请填入脚本 SHA256 表：" -ForegroundColor Yellow
        Get-FileSha256 $dest
    } else {
        $actual = Get-FileSha256 $dest
        if ($actual -ne $expected.ToLower()) {
            Remove-Item -LiteralPath $dest -Force
            throw "[error] SHA256 校验失败：$name`n       期望 $expected`n       实际 $actual"
        }
        Write-Host "[ok] $name 校验通过"
    }
}

Write-Host ''
Write-Host '==> 完成。文件清单：'
Get-ChildItem -LiteralPath $TargetDir -File | Format-Table Name, Length -AutoSize

# 打印所有文件的 SHA256，便于发布前回填脚本。
Write-Host ''
Write-Host '==> 各文件 SHA256（供填入脚本 SHA256 表）：'
foreach ($name in $Urls.Keys) {
    $dest = Join-Path $TargetDir $name
    $hash = Get-FileSha256 $dest
    Write-Host ("  '{0}' = '{1}'" -f $name, $hash)
}

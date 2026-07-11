# STTmini 发布脚本（AGENTS.md §10.5）。Windows 原生等价物（PowerShell）。
# 本脚本是 scripts/publish.sh 的 Windows 原生等价物，不依赖 bash / zip / sha256sum。
# 本地用户用本脚本即可完成 Windows 发布；CI 仍走 publish.sh（同一份 dotnet 命令）。
#
# 用法：
#   pwsh scripts/publish.ps1                          # 版本取自 Directory.Build.props，默认 win-x64
#   pwsh scripts/publish.ps1 -Version 0.1.0           # 覆盖版本号
#   pwsh scripts/publish.ps1 -Version 0.1.0 -Rids win-x64,linux-x64
#
# 产物：dist/STTmini-<rid>-<version>.{zip|tar.gz}，内含 app + models/。

[CmdletBinding()]
param(
    # 版本号；为空则使用 Directory.Build.props 里的 VersionPrefix（CI 用 publish.sh 强制覆盖）。
    [string]$Version,
    # 目标 RID 列表；默认 win-x64。
    [string[]]$Rids = @('win-x64'),
    # 跳过 dotnet test（默认会先跑 Core 单测）。
    [switch]$SkipTest
)

$ErrorActionPreference = 'Stop'
$global:ProgressPreference = 'SilentlyContinue' # IWR 下载模型时关闭进度

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Dist = Join-Path $Root 'dist'
$ModelsCache = Join-Path $Root '.models-cache'
$AppProject = Join-Path $Root 'src\STTmini.App\STTmini.App.csproj'

if ([string]::IsNullOrEmpty($Version)) {
    # 解析 Directory.Build.props 的 VersionPrefix 作为默认版本。
    $props = Join-Path $Root 'Directory.Build.props'
    if (Test-Path -LiteralPath $props) {
        $m = [regex]::Match((Get-Content -LiteralPath $props -Raw), '<VersionPrefix>\s*([^<\s]+)')
        if ($m.Success) { $Version = $m.Groups[1].Value }
    }
    if ([string]::IsNullOrEmpty($Version)) {
        throw '未指定 -Version 且无法从 Directory.Build.props 解析 VersionPrefix'
    }
    Write-Host "==> 版本号取自 Directory.Build.props：$Version"
}

# 打包 zip：用 .NET 自带 System.IO.Compression.ZipFile（Windows 无独立 zip.exe）。
function Compress-ToZip {
    param([string]$SourceDir, [string]$ZipPath)
    Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($SourceDir, $ZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal, $false)
}

# 打包 tar.gz：优先用 Windows 内置 bsdtar（%WINDIR%\System32\tar.exe）。
# 必须显式定位 bsdtar：PATH 里的 `tar` 若指向 Git for Windows 的 GNU tar，会把
# `D:\path` 误解析成 "host:path" 远程语法（GNU tar 的盘符坑）。bsdtar 无此问题。
function Resolve-TarExe {
    $sys = Join-Path $env:WINDIR 'System32\tar.exe'
    if (Test-Path -LiteralPath $sys) { return $sys }
    # 退路：PATH 里的 tar（非 Windows 或旧系统）。
    $found = Get-Command tar -ErrorAction SilentlyContinue
    if ($found) { return $found.Source }
    throw '未找到 tar：发布 linux/osx 产物需要 tar（Windows 10+ 内置于 System32）'
}

function Compress-ToTarGz {
    param([string]$SourceDir, [string]$TarPath)
    if (Test-Path -LiteralPath $TarPath) { Remove-Item -LiteralPath $TarPath -Force }
    $tarExe = Resolve-TarExe
    # -C 切到源目录后打包其内容（不含源目录名本身），与 publish.sh 的 `tar -czf ... -C <dir> .` 对齐。
    & $tarExe -czf $TarPath -C $SourceDir .
    if ($LASTEXITCODE -ne 0) { throw "tar 退出码 $LASTEXITCODE" }
}

# ---- 清理产物目录 ----
if (Test-Path -LiteralPath $Dist) { Remove-Item -LiteralPath $Dist -Recurse -Force }
New-Item -ItemType Directory -Path $Dist -Force | Out-Null

# ---- 单元测试（默认）----
if (-not $SkipTest) {
    Write-Host '==> 0/5 单元测试（STTmini.Core.Tests）'
    $testProject = Join-Path $Root 'src\STTmini.Core.Tests\STTmini.Core.Tests.csproj'
    if (Test-Path -LiteralPath $testProject) {
        & dotnet test $testProject -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "单元测试失败（退出码 $LASTEXITCODE）" }
    } else {
        Write-Host '[warn] 未找到 STTmini.Core.Tests，跳过测试' -ForegroundColor Yellow
    }
}

# ---- 准备模型源（多 RID 共用）----
# 优先复用仓库根的 models/（本地开发已下载过则免重复下载 ~234MB）。
# 校验由 models.ps1 的 SHA256 逻辑兜底：若 models/ 不完整，会被识别并补下。
# CI 环境通常无 models/，自然回退到下载 .models-cache。
$ModelsSource = Join-Path $Root 'models'
if (-not (Test-Path -LiteralPath $ModelsSource) -or -not (Get-ChildItem -LiteralPath $ModelsSource -File -ErrorAction SilentlyContinue)) {
    Write-Host '==> 1/5 下载模型到缓存（未发现本地 models/）'
    $ModelsSource = $ModelsCache
    & pwsh -NoProfile -File (Join-Path $Root 'scripts\models.ps1') -TargetDir $ModelsCache
    if ($LASTEXITCODE -ne 0) { throw "模型下载失败（退出码 $LASTEXITCODE）" }
} else {
    Write-Host "==> 1/5 复用本地模型：$ModelsSource"
    # 复用也走一遍 models.ps1：存在但损坏/缺失时按 SHA256 校验并补下。
    & pwsh -NoProfile -File (Join-Path $Root 'scripts\models.ps1') -TargetDir $ModelsSource
    if ($LASTEXITCODE -ne 0) { throw "模型校验/补全失败（退出码 $LASTEXITCODE）" }
}

foreach ($rid in $Rids) {
    Write-Host "==> 发布 RID=$rid 版本=$Version"

    # ---- dotnet publish（AGENTS.md §10.2 硬约束）----
    Write-Host '==> 2/5 编译'
    $pubArgs = @('publish', $AppProject,
        '-c', 'Release',
        '-r', $rid,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        "-p:Version=$Version",
        '--nologo')
    & dotnet @pubArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }

    $publishOut = Join-Path $Root "src\STTmini.App\bin\Release\net10.0\$rid\publish"
    if (-not (Test-Path -LiteralPath $publishOut)) {
        throw "未找到发布输出目录：$publishOut"
    }

    # ---- 复制 models/ 进发布目录 ----
    Write-Host '==> 3/5 嵌入模型'
    $modelsOut = Join-Path $publishOut 'models'
    if (Test-Path -LiteralPath $modelsOut) { Remove-Item -LiteralPath $modelsOut -Recurse -Force }
    New-Item -ItemType Directory -Path $modelsOut -Force | Out-Null
    Copy-Item -Path (Join-Path $ModelsSource '*') -Destination $modelsOut -Recurse -Force

    # ---- 剔除 pdb（发布产物不含调试符号）----
    # Release 默认仍生成 portable pdb（自家代码 + NuGet 原生库如 HarfBuzzSharp/SkiaSharp 都带），
    # 打包前统一删除；不改编译行为，本地调试与开发体验不受影响。
    Write-Host '==> 4/5 剔除调试符号（pdb）'
    Get-ChildItem -LiteralPath $publishOut -Recurse -Filter '*.pdb' -File |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

    # ---- 打包 ----
    Write-Host '==> 5/5 打包'
    switch -Wildcard ($rid) {
        'win-*'   { $archive = Join-Path $Dist "STTmini-$rid-$Version.zip"    ; Compress-ToZip    $publishOut $archive }
        'linux-*' { $archive = Join-Path $Dist "STTmini-$rid-$Version.tar.gz"; Compress-ToTarGz  $publishOut $archive }
        'osx-*'   { $archive = Join-Path $Dist "STTmini-$rid-$Version.tar.gz"; Compress-ToTarGz  $publishOut $archive }
        default   { throw "不支持的 RID：$rid" }
    }
    Write-Host "    -> $archive"
}

Write-Host '==> 全部完成。产物：'
Get-ChildItem -LiteralPath $Dist -File | Format-Table Name, Length -AutoSize

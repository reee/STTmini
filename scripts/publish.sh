#!/usr/bin/env bash
# STTmini 发布脚本（AGENTS.md §10.5）。本地与 CI 调用同一份脚本。
# 用法：scripts/publish.sh <version> [rid...]
#   version：版本号（如 0.1.0），通过 -p:Version 覆盖 Directory.Build.props
#   rid...：目标 RID（默认 win-x64 linux-x64）
# 产物：dist/STTmini-<rid>-<version>.{zip|tar.gz}，内含 app + models/。

set -euo pipefail

VERSION="${1:?用法: publish.sh <version> [rid...]}"
shift || true
RIDS=("$@")
[[ ${#RIDS[@]} -eq 0 ]] && RIDS=(win-x64 linux-x64)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/dist"
MODELS_CACHE="$ROOT/.models-cache"
APP_PROJECT="$ROOT/src/STTmini.App/STTmini.App.csproj"

rm -rf "$DIST"
mkdir -p "$DIST"

# 1) 模型下载到缓存目录（多 RID 共用，避免重复下载）
echo "==> 1/5 下载模型到缓存"
bash "$ROOT/scripts/models.sh" "$MODELS_CACHE"

for rid in "${RIDS[@]}"; do
  echo "==> 发布 RID=$rid 版本=$VERSION"

  # 2) dotnet publish（显式 RID、self-contained、单文件，禁止 trim/AOT，AGENTS.md §10.2）
  echo "==> 2/5 编译"
  dotnet publish "$APP_PROJECT" \
    -c Release \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="$VERSION"

  PUBLISH_OUT="$ROOT/src/STTmini.App/bin/Release/net10.0/$rid/publish"

  # 3) 复制 models/ 进发布目录
  echo "==> 3/5 嵌入模型"
  mkdir -p "$PUBLISH_OUT/models"
  cp -r "$MODELS_CACHE/." "$PUBLISH_OUT/models/"

  # 4) 剔除 pdb（发布产物不含调试符号）
  #    Release 默认仍生成 portable pdb（自家代码 + NuGet 原生库如 HarfBuzzSharp/SkiaSharp 都带），
  #    打包前统一删除；不改编译行为，本地调试与开发体验不受影响。
  echo "==> 4/5 剔除调试符号（pdb）"
  find "$PUBLISH_OUT" -type f -name '*.pdb' -delete

  # 5) 打包
  echo "==> 5/5 打包"
  case "$rid" in
    win-*)
      archive="$DIST/STTmini-$rid-$VERSION.zip"
      (cd "$PUBLISH_OUT" && zip -qr "$archive" .)
      ;;
    linux-*|osx-*)
      archive="$DIST/STTmini-$rid-$VERSION.tar.gz"
      tar -czf "$archive" -C "$PUBLISH_OUT" .
      ;;
    *)
      echo "[error] 不支持的 RID：$rid" >&2
      exit 1
      ;;
  esac

  echo "    → $archive"
done

echo "==> 全部完成。产物："
ls -lh "$DIST"

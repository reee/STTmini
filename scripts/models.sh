#!/usr/bin/env bash
# 下载并校验 STTmini 所需的模型文件（AGENTS.md §9.3）。
#
# 用法：scripts/models.sh [目标目录] [镜像源前缀]
#   目标目录      默认 ./models
#   镜像源前缀    默认空（用官方源）；可设为 https://hf-mirror.com
#                 也可用环境变量 STTMINI_MIRROR 指定。
#
# 校验：每个文件按 SHA256 验证（见下方 SHA256 表）。
#       占位为空时跳过校验；建议下载后运行本脚本，末尾会打印实际 SHA256 供填入。
#
# 手动放置：若网络受限，可手工下载文件放入目标目录，本脚本会跳过已存在文件（仅校验）。
#
# 国内网络提示：HuggingFace 直连可能被重置。
#   export STTMINI_MIRROR=https://hf-mirror.com
#   bash scripts/models.sh
# 或用浏览器/代理下载后放入目录，再跑本脚本校验。

set -euo pipefail

TARGET_DIR="${1:-./models}"
MIRROR="${2:-${STTMINI_MIRROR:-}}"
mkdir -p "$TARGET_DIR"

# HuggingFace 官方前缀与可替换镜像前缀。
HF_BASE="https://huggingface.co/csukuangfj/sherpa-onnx-paraformer-zh-2023-09-14/resolve/main"
HF_PREFIX="${MIRROR:-https://huggingface.co}"
# VAD 模型在 GitHub releases（不受 HF 影响）。
VAD_URL="https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx"

# 文件 → 下载 URL
declare -A URLS=(
  ["model.int8.onnx"]="$HF_PREFIX/csukuangfj/sherpa-onnx-paraformer-zh-2023-09-14/resolve/main/model.int8.onnx"
  ["tokens.txt"]="$HF_PREFIX/csukuangfj/sherpa-onnx-paraformer-zh-2023-09-14/resolve/main/tokens.txt"
  ["am.mvn"]="$HF_PREFIX/csukuangfj/sherpa-onnx-paraformer-zh-2023-09-14/resolve/main/am.mvn"
  ["silero_vad.onnx"]="$VAD_URL"
)

# 文件 → SHA256（实测值，来自 huggingface.co/csukuangfj 与 GitHub asr-models release）。
declare -A SHA256=(
  ["model.int8.onnx"]="f36a0433bcf096bd6d6f11b80a3ac8bed110bdca632fe0d731df8d1a84475945"
  ["tokens.txt"]="59aba8873a2ed1e122c25fee421e25f283b63290efbde85c1f01a853d83cb6e6"
  ["am.mvn"]="29b3c740a2c0cfc6b308126d31d7f265fa2be74f3bb095cd2f143ea970896ae5"
  ["silero_vad.onnx"]="9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6"
)

ORDER=(model.int8.onnx tokens.txt am.mvn silero_vad.onnx)

verify_one() {
  local name="$1"
  local dest="$TARGET_DIR/$name"
  local expected="${SHA256[$name]:-}"

  if [[ ! -f "$dest" ]]; then
    return 2  # 不存在
  fi

  if [[ -z "$expected" ]]; then
    return 0  # 存在但无校验值，视为通过
  fi

  local actual
  actual=$(sha256sum "$dest" | awk '{print $1}')
  if [[ "$actual" == "$expected" ]]; then
    return 0
  fi
  return 1  # 校验失败
}

download() {
  local name="$1"
  local dest="$TARGET_DIR/$name"
  local url="${URLS[$name]}"

  # 先看是否已存在且通过校验
  if verify_one "$name"; then
    echo "[skip] $name 已存在且校验通过"
    return 0
  fi

  echo "[download] $name ← $url"
  if ! curl -fL --retry 3 -o "$dest" "$url"; then
    echo "[error] 下载失败：$name" >&2
    echo "        若网络受限，可用浏览器下载该文件放入 $TARGET_DIR/ 后重跑本脚本。" >&2
    exit 1
  fi

  local expected="${SHA256[$name]:-}"
  if [[ -n "$expected" ]]; then
    local actual
    actual=$(sha256sum "$dest" | awk '{print $1}')
    if [[ "$actual" != "$expected" ]]; then
      echo "[error] SHA256 校验失败：$name" >&2
      echo "       期望 $expected" >&2
      echo "       实际 $actual" >&2
      rm -f "$dest"
      exit 1
    fi
    echo "[ok] $name 校验通过"
  else
    echo "[info] $name 已下载（未配置 SHA256）。实际值如下，请填入脚本 SHA256 表："
    sha256sum "$dest"
  fi
}

if [[ -n "$MIRROR" ]]; then
  echo "==> 使用镜像源：$MIRROR"
fi
echo "==> 目标目录：$TARGET_DIR"

for name in "${ORDER[@]}"; do
  download "$name"
done

echo ""
echo "==> 完成。文件清单："
ls -lh "$TARGET_DIR"

# 打印所有文件的 SHA256，便于发布前回填脚本。
echo ""
echo "==> 各文件 SHA256（供填入脚本 SHA256 表）："
for name in "${ORDER[@]}"; do
  printf '  ["%s"]="%s"\n' "$name" "$(sha256sum "$TARGET_DIR/$name" | awk '{print $1}')"
done

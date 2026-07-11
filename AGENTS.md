# AGENTS.md — STTmini

> 本文件是 STTmini 项目的实现基准。所有架构、模块边界、构建发布流程均以此为准。
> 后续实现应严格遵循本文档；如需调整，先修订本文档再改代码。

---

## 1. 项目概述

**STTmini** = Speech-To-Text + Mini。

一个跨平台（Windows / Debian-Ubuntu）的**离线**中文语音转文本桌面程序，面向简体中文视频的字幕与文本识别。核心设计目标：

- **Mini**：发布体积尽可能小，避免打包非必要依赖。
- **跨平台**：基于 .NET 10 + Avalonia，Windows 与 Linux 同一份代码库。
- **离线**：所有识别在本地完成，不依赖网络服务。
- **简体中文优先**：界面与识别均面向简体中文，暂不考虑多语言。

### 1.1 名称语义

- **STT** = Speech to Text
- **Mini** = 应用尽可能小（体积、依赖、复杂度）

---

## 2. 技术栈

| 层 | 选型 |
|----|------|
| 运行时 | .NET 10（全部项目 TFM = `net10.0`，不使用 netstandard） |
| UI 框架 | Avalonia 12（最新稳定版 12.1.x） |
| MVVM | CommunityToolkit.Mvvm（source generator，无运行时依赖） |
| DI | Microsoft.Extensions.DependencyInjection |
| 日志 | Microsoft.Extensions.Logging + 手写极简文件 logger provider（不引入 Serilog） |
| 序列化 | System.Text.Json（配置文件） |
| 语音识别 | sherpa-onnx（NuGet：`org.k2fsa.sherpa.onnx`，版本 = 1.13.4，要求 ≥ 1.13.3） |
| 模型 | Paraformer-zh int8（`sherpa-onnx-paraformer-zh-2023-09-14`） + Silero VAD int8 |
| 音频解码 | 外部 ffmpeg（用户自备，不打包） |

### 2.1 sherpa-onnx NuGet 包

托管包：`org.k2fsa.sherpa.onnx`（命名空间 `SherpaOnnx`）。

原生运行时包（**两个都无条件引用**，NuGet 的 RID 图谱会按发布目标自动选取对应平台的原生库）：

- `org.k2fsa.sherpa.onnx.runtime.win-x64`
- `org.k2fsa.sherpa.onnx.runtime.linux-x64`

> **禁止**额外引用 `Microsoft.ML.OnnxRuntime`。onnxruntime 已被静态链接进上述运行时包，额外引用会导致版本冲突与加载冲突。

---

## 3. 项目结构

```
STTmini/
├── Directory.Build.props          # 集中管理版本号
├── AGENTS.md
├── .gitignore                     # 忽略 models/、发布产物等
├── scripts/
│   ├── publish.sh                 # 本地发布脚本（CI 调用同一脚本）
│   └── models.sh                  # 按 SHA256 下载模型文件
├── src/
│   ├── STTmini.Core/              # 引擎层（无 UI 依赖）
│   ├── STTmini.App/               # Avalonia UI 层
│   └── STTmini.Core.Tests/        # Core 纯逻辑单元测试
├── models/                        # 本地开发用模型目录（不进 Git）
└── .github/
    └── workflows/
        └── release.yml            # 仅 Release 发布时触发
```

### 3.1 项目职责

#### `STTmini.Core`（TFM: net10.0）

引擎层，**不引用** Avalonia 或任何 UI 库。职责：

- ffmpeg 调用与音频提取
- VAD 分段
- ASR 识别（通过接口封装 sherpa-onnx）
- SRT / 纯文本格式化
- 段切分与时间戳计算
- 配置读写
- 模型路径解析
- 日志 provider 实现

#### `STTmini.App`（TFM: net10.0）

UI 层，引用 `STTmini.Core`。职责：

- Avalonia 视图与 ViewModel
- DI 容器装配
- 用户交互、进度反馈、错误提示
- portable/XDG 路径策略的入口判断

#### `STTmini.Core.Tests`（TFM: net10.0）

引用 `STTmini.Core`。仅测试**纯逻辑**，不依赖原生库或真实模型：

- SRT 格式化
- 纯文本拼接（含段落分隔规则）
- 段切分（含超长段 25s 重切）
- 时间戳偏移计算
- ffmpeg 命令构造
- 模型路径解析
- 配置序列化

---

## 4. 核心架构

### 4.1 处理流水线

单次转录的端到端流程（全部在后台 `Task` 执行）：

```
用户选择输入文件
   │
   ▼
[1] ffmpeg 解码    视频/音频 → 16kHz mono PCM（temp WAV）→ float[]
   │                  (一次性 ffmpeg 调用)
   ▼
[2] VAD 分段       Silero VAD → SpeechSegment[]（每个含 .Start 与 .Samples）
   │                  注意：Silero VAD 默认 MaxSpeechDuration=5s 会自动切分；
   │                  v1 把它显式设为 30s，让超长段统一由 [3] 的 25s 窗口处理（单一切分策略）。
   │
   ▼
[3] 超长段重切     若 segment 时长 > 25s → 固定窗口切为多个子段
   │
   ▼
[4] ASR 识别       逐段（或逐子段）送入 OfflineRecognizer
   │                  每段得到 Result.Text / Result.Tokens / Result.Timestamps
   ▼
[5] 时间戳修正     每个 token 时间戳 += 段全局偏移（seg.Start 或子段偏移）
   │
   ▼
[6] 输出格式化     纯文本（默认）/ SRT（带时间戳）
```

### 4.2 模块边界（Core 内部）

每个模块是一个**可独立测试的纯逻辑单元** + 一个**薄原生封装**：

| 模块 | 纯逻辑（可测） | 原生封装（接口隔离） |
|------|---------------|---------------------|
| 音频提取 | `FfmpegCommandBuilder` | `IAudioExtractor`（封装 ffmpeg 进程调用） |
| VAD | — | `IVoiceActivityDetector`（封装 `SherpaOnnx.VoiceActivityDetector`） |
| ASR | — | `IRecognizer`（封装 `SherpaOnnx.OfflineRecognizer`） |
| 段切分 | `SegmentChunker`（25s 重切） | — |
| 时间戳 | `TimestampMath`（偏移、cue 边界） | — |
| SRT 格式化 | `SrtFormatter` | — |
| 纯文本格式化 | `PlainTextFormatter` | — |
| 模型路径 | `ModelPathResolver` | — |
| 配置 | `Settings`（POCO）+ `SettingsStore` | — |

### 4.3 接口设计原则（配合 Q8 GPU seam 与 Q17 可测性）

原生类（`OfflineRecognizer`、`VoiceActivityDetector`）一律包在接口后面。理由：

1. **可测性**：纯逻辑测试通过 mock 接口完成，不依赖真实模型/原生库。
2. **Provider seam**：v1 仅 CPU；未来加 GPU 时，只需新增一个 `IRecognizer` 实现，不破坏调用方。

接口返回的 DTO（如 `RecognitionResult { Text, Tokens[], Timestamps[] }`）由 Core 自有，**不**把 sherpa-onnx 的结构体泄露到上层。

### 4.4 认识器生命周期

- `OfflineRecognizer` **每次运行新建**，运行结束 `Dispose`。
- **不跨并发调用共享**（sherpa-onnx 的 recognizer 非线程安全，且 native 运行时初始化慢）。
- 单次运行内：一个 worker task 串行处理所有段。

---

## 5. 数据结构与算法约定

### 5.1 ASR 输出消费

`OfflineRecognizer` 单段调用形态：

```
var recognizer = new OfflineRecognizer(config);
OfflineStream stream = recognizer.CreateStream();
stream.AcceptWaveform(sampleRate: 16000, samples);
recognizer.Decode(stream);
OfflineRecognizerResult result = stream.Result;
  → result.Text        : string   全文（含中文标点）
  → result.Tokens      : string[] 每 token
  → result.Timestamps  : float[]  每 token 时间戳（秒，段内相对）
  → result.Durations   : float[]  每 token 时长（秒，可能为 null）
```

> 配置要点（v1.13.4 实测 API）：
> - `config.ModelConfig.Paraformer.Model` = `model.int8.onnx` 路径（`OfflineParaformerModelConfig` **仅有** `.Model` 字段）。
> - `config.ModelConfig.Tokens` = `tokens.txt` 路径（Tokens 在 `OfflineModelConfig` 上，**不在** Paraformer 子配置上）。
> - `config.ModelConfig.NumThreads` = 1。
> - 离线结果类型 `OfflineRecognizerResult` **没有** `.Json` 属性（`.Json` 仅存在于在线结果）。

- **全局时间戳** = `seg.StartSeconds + result.Timestamps[i]`（VAD 段）或 `子段全局起点 + result.Timestamps[i]`（超长段重切后）。
  - sherpa-onnx 原生 `SpeechSegment.Start` 是 **int 样本偏移**（非秒）；封装层将其换算为秒 `Start / 16000f` 后填入 Core 自有的 `SpeechSegment.StartSeconds`。
- Paraformer 原生输出中文标点，**不**需要额外的标点模型。

### 5.2 SRT 格式化规则

- **单 VAD 段 → 一个 SRT cue**（超长段重切后，每个子段 → 一个 cue）。
- **cue 边界**：取段内首 token 时间戳为 cue 起点、末 token 时间戳为终点，加上段全局偏移。**不**用 VAD 段边界。
- **时间格式**：`HH:MM:SS,mmm`（逗号分隔毫秒）。
- **超长段处理**：段时长 > 25s 且无内部静音 → 固定 25s 窗口切分子段；每个子段独立识别，独立成 cue。
- **v1 仅支持 SRT**，不支持 VTT（未来可加，数据相同仅分隔符不同）。

### 5.3 纯文本格式化规则

- 按段顺序拼接各段 `Result.Text`。
- **段落分隔启发式**：相邻两段之间的静音间隔 > **2 秒** → 插入空行（段落分隔）；否则单换行。
- 不做 NLP 级别的句子重排。

### 5.4 ffmpeg 调用约定

- 一次性调用：`ffmpeg -y -vn -i <input> -ar 16000 -ac 1 -f wav <temp.wav>`
  - `-y`：覆盖已存在的 temp WAV（重跑同一输入时不报错）。
  - `-vn`：丢弃视频流（输入为视频时避免不必要的解码）。
- 产出 temp WAV 后**全量载入内存**为 `float[]`（~5.7 MB/分钟）。
- 内存代价已知：1 小时视频 ~345MB RAM。若未来成问题，升级为 temp 文件流式读取。
- **错误处理**（类型化异常）：
  - ffmpeg 不在 PATH / 设置路径无效 → `FfmpegNotFoundException`（UI 提示去 Settings 配置）
  - ffmpeg 返回非零退出码 → `AudioExtractionException`（含截断的 stderr 尾部，非全量日志）

---

## 6. UI 设计（STTmini.App）

### 6.1 界面语言

仅简体中文。不考虑多语言资源机制。

### 6.2 工作流

**单文件**（v1 不做批量）。主流程：

1. 选择输入文件（文件选择对话框 + 拖放）。
2. 点击转录（进度 + 可取消）。
3. 内联查看结果（纯文本 / SRT 切换）。
4. 保存为 `.txt` 或 `.srt`。

### 6.3 进度反馈

分阶段真实进度（非不确定进度条），每阶段有中文标签：

- `解码音频…`
- `语音活动检测…`
- `识别中…（段 i / 总 N）`

ASR 阶段每完成一段即通过 `IProgress<T>` 推送一次，结果面板**实时填充**（不等全部完成）。

### 6.4 取消

- 取消按钮在转录中可用。
- 取消粒度：**段边界**（当前段识别完成后停止，不中断段内识别）。
- 取消后已识别的段可保留显示。

### 6.5 Settings 页

| 项 | 说明 |
|----|------|
| ffmpeg 路径 | 自动检测 PATH，用户可手动覆盖。未配置时转录入口给出明显提示。 |
| 模型目录 | v1 只读（"已随程序附带"）。显示当前模型路径。 |
| 默认输出格式 | 纯文本（默认）/ SRT。 |

---

## 7. 线程模型

- UI 线程：Avalonia 主线程，不执行任何 CPU 密集工作。
- 转录流水线：单次 `Task.Run`，跑在线程池。
- 进度回传：`IProgress<T>`（Avalonia 自动 marshal 到 UI 线程）。
- 取消：`CancellationToken`，传入 worker；在段循环边界检查 `ThrowIfCancellationRequested()`。
- 约束：**单 worker per run**，recognizer per-run 新建与释放。

---

## 8. 配置与数据持久化

### 8.1 配置文件位置（按平台分流）

| 平台 | 路径 |
|------|------|
| Windows | `<程序运行目录>/STTmini.settings.json`（portable） |
| Linux | `~/.config/STTmini/settings.json`（XDG） |

判断逻辑：

```
RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
  ? AppContext.BaseDirectory
  : Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "STTmini")
```

### 8.2 配置项（极简）

- `FfmpegPathOverride`（string?，null = 用 PATH 自动检测）
- `DefaultOutputFormat`（"PlainText" | "Srt"，默认 "PlainText"）
- `LastInputDirectory`（string?）

**不**记录最近文件列表、不记录窗口几何。

### 8.3 模型目录

- **两边都跟随程序目录**（`AppContext.BaseDirectory/models/`）。
- v1 用户不可改（已随发布包附带）。

### 8.4 日志文件位置（按平台分流）

| 平台 | 路径 |
|------|------|
| Windows | `<程序运行目录>/logs/` |
| Linux | `~/.local/share/STTmini/logs/`（XDG data） |

- 默认 Information 级，调试期可调 Debug。
- 单文件滚动（固定大小或保留最近 N 个）。
- sherpa-onnx 的 native 错误冒到 managed 层后，捕获并用 `ILogger.LogError(ex, ...)` 记录。

---

## 9. 模型文件

### 9.1 文件清单（随发布包附带）

| 文件 | 来源 | 大小 | SHA256（实测） |
|------|------|------|------|
| `model.int8.onnx` | HuggingFace `csukuangfj/sherpa-onnx-paraformer-zh-2023-09-14` | ~233MB | `f36a0433…475945` |
| `tokens.txt` | 同上 | ~74KB | `59aba887…3cb6e6` |
| `am.mvn` | 同上 | ~11KB | `29b3c740…96ae5` |
| `silero_vad.onnx` | GitHub `k2-fsa/sherpa-onnx` release `asr-models` | ~629KB | `9e2449e1…1fd6` |

> **总模型体积 ~234MB**（paraformer-zh int8 实测 233MB，大于早先估计的 68MB——此为该模型真实体积）。
> 完整 SHA256 见 `scripts/models.sh`。
> ⚠️ 这与 Mini 体积目标有张力；若后续要压缩，可评估切换更小的中文 paraformer 变体（见 §12）。

### 9.2 量化决策（已定）

- Paraformer：**int8**（fp32 ~220MB，WER 几乎无差，体积近 3 倍，不划算）。
- VAD：**int8**。
- **不使用** Fun-ASR-Nano（见 §12 评估记录）。

### 9.3 发布时的模型获取

- 模型**不进 Git**（`.gitignore` 忽略 `models/`）。
- 发布脚本（`scripts/models.sh`）下载来源：
  - paraformer 三件套：`https://huggingface.co/csukuangfj/sherpa-onnx-paraformer-zh-2023-09-14/resolve/main/<file>`
  - Silero VAD：`https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx`
  - 支持 `STTMINI_MIRROR` 环境变量指定 HF 镜像前缀（如 `https://hf-mirror.com`），便于受限网络。
  - 按 SHA256 校验；网络不通时可手动放置文件到目录后重跑脚本（已存在则跳过）。
- 本地开发：跑 `scripts/models.sh ./models` 或手动下载放入 `models/`。

---

## 10. 打包与发布

### 10.1 交付物形态

**每平台一个压缩包**，内含 app 二进制 + `models/` 文件夹：

| 平台 | 交付物 |
|------|--------|
| Windows | `STTmini-win-x64-<version>.zip`（exe + models/） |
| Linux | `STTmini-linux-x64-<version>.tar.gz`（可执行文件 + models/） |

> 注意：因为模型以 loose 文件形式附带，Windows 交付物是"exe + 同目录 models 文件夹"，**不是**真正的单文件。此为已确认的折衷（换取首启动零网络体验）。
> **不产** `.deb`（tarball 在 Debian/Ubuntu 解压即用，self-contained 无系统库依赖，已满足"支持 Debian/Ubuntu"）。

### 10.2 发布参数

```
dotnet publish src/STTmini.App -c Release \
  -r <win-x64|linux-x64> \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version=<version>
```

硬约束：

- **显式 RID**（单文件必须 OS/arch 特定）。
- **Self-contained**（目标机无需装 .NET）。
- **单文件 + IncludeNativeLibrariesForSelfExtract**（把 native 库打进单文件，运行时解压）。
- **禁止** `PublishTrimmed` / `PublishAot`（sherpa-onnx 的 P/Invoke wrapper 不兼容 trim/AOT）。
- 模型文件 loose 放在 exe 同目录（不进单文件）。

### 10.3 版本管理

- `Directory.Build.props` 集中定义版本号。
- CI 发布时 `-p:Version=<version>` 覆盖。
- 不使用 MinVer（避免额外依赖与认知成本）。

### 10.4 CI

- **GitHub Actions，仅 Release 发布时触发**（不在普通 push/tag 时跑）。
- 矩阵构建：`win-x64` + `linux-x64`。
- 调用同一份 `scripts/publish.sh`（保证本地与 CI 一致）。
- 产出上传到 GitHub Release（用 `softprops/action-gh-release`）。
- **第三方 action 一律 pin 到完整 commit SHA**（`@v2` 等浮动 tag 会被更新、有供应链风险）。SHA 见 `.github/workflows/release.yml`，升级时人工核对并更新。

### 10.5 发布脚本职责（`scripts/publish.sh`）

1. 调用 `scripts/models.sh` 下载并校验模型到临时目录。
2. 对每个 RID 执行 `dotnet publish`。
3. 把 `models/` 复制进各 publish 输出目录。
4. 打包为 `.zip`（Windows）/ `.tar.gz`（Linux）。

---

## 11. 错误处理与日志

### 11.1 异常分类（Core 定义，UI 针对性提示）

| 异常 | 触发 | UI 提示 |
|------|------|--------|
| `FfmpegNotFoundException` | PATH 与设置均无 ffmpeg | "未找到 ffmpeg，请在 Settings 中配置路径" |
| `AudioExtractionException` | ffmpeg 非零退出 | "音频提取失败：<截断 stderr>" |
| `ModelNotFoundException` | 模型文件缺失 | "模型文件缺失，请重新安装或检查程序目录" |
| `RecognizerInitializationException` | sherpa-onnx 初始化失败 | "识别引擎初始化失败，详见日志" |

### 11.2 日志

- 抽象：`Microsoft.Extensions.Logging.ILogger<T>`。
- 实现：**手写极简文件 logger provider**（约 30 行），不引入 Serilog。
- Native 错误：冒到 managed 层后 `ILogger.LogError(ex, "...")`。

---

## 12. 备选方案评估记录

### 12.1 Fun-ASR-Nano-2512（已否决，保留记录）

曾评估用 `foryoung365/Fun-ASR-Nano-2512-int4-onnx` 替代 Paraformer。结论：**不采用**。

- **时间戳是均匀插值**（`t[i] ≈ i*D/N`），非真实声学对齐 → SRT cue 时间失准，伤及核心价值。
- **体积违背 Mini**：int8 官方 ~948MB（14 倍），foryoung365 int4 ~400-500MB（6-7 倍）。
- **纯 CPU 下 int4 未验证**：foryoung365 仅在 Windows+CUDA 下测过；v1 锁定 CPU-only。
- **非官方 + 无 license**：商业再分发有法律风险。
- **成熟度不足**：模型 2025-12 发布，1.13.3 仍在修字幕 bug，社区报告 int8 重复输出（#3066）。

v1 维持 Paraformer-zh int8。

---

## 13. 实现顺序建议

后续按本文档实现时，建议顺序：

1. 解决方案骨架：`Directory.Build.props` + 三个项目 + `.gitignore`。
2. Core 纯逻辑模块 + 单测：`SegmentChunker`、`TimestampMath`、`SrtFormatter`、`PlainTextFormatter`、`FfmpegCommandBuilder`、`ModelPathResolver`、`Settings`/`SettingsStore`。
3. Core 原生封装接口与实现：`IAudioExtractor`、`IVoiceActivityDetector`、`IRecognizer`。
4. Core 流水线编排（`Task.Run` + `IProgress` + `CancellationToken`）。
5. App：DI 装配、主窗口、Settings 页、进度与结果视图。
6. 发布脚本与 GitHub Actions workflow。
7. 真实模型手动冒烟测试（发布前）。

---

## 14. 实现现状（首版已落地）

以下为依据本文件实际完成的代码地图，供后续维护导航。

### 14.1 已实现模块

**STTmini.Core**（`src/STTmini.Core/`，TFM net10.0）

| 命名空间 | 类型 | 职责 |
|----------|------|------|
| `Audio` | `AudioConstants` | 16kHz 采样率常量 |
| `Audio` | `FfmpegCommandBuilder` | 构造 ffmpeg 参数（纯逻辑） |
| `Audio` | `FfmpegLocator` | 解析 ffmpeg 路径（覆盖→PATH） |
| `Audio` | `FfmpegAudioExtractor` | `IAudioExtractor` 实现，跑 ffmpeg + 读 WAV→float[] |
| `Audio` | `WavReader` | 16kHz mono PCM16 WAV → float[] |
| `Audio` | `SegmentChunker` / `ChunkedSegment` | 25s 超长段重切（纯逻辑） |
| `Audio` | `SpeechSegment` / `IVoiceActivityDetector` / `SherpaVoiceActivityDetector` | VAD 抽象 + Silero 实现 |
| `Audio` | `IAudioExtractor` | 音频提取接口 |
| `Configuration` | `Settings` / `OutputFormat` / `OutputFormats` | 设置 POCO + 枚举 + UI 列表 |
| `Configuration` | `SettingsStore` | 配置读写（损坏回退默认） |
| `Configuration` | `AppPaths` | 平台相关路径（portable/XDG） |
| `Errors` | `STTminiException` 及四个子类 | 异常分类（§11.1） |
| `Logging` | `FileLoggerProvider` | 手写文件 logger（含滚动） |
| `Models` | `ModelPathResolver` / `ModelFileNames` | 模型路径解析与存在性校验 |
| `Pipeline` | `TranscriptionEngine` | 流水线编排（§4.1 / §7） |
| `Pipeline` | `TranscriptionProgress` / `TranscriptionStage` | 进度报告 |
| `Pipeline` | `ITranscriptionComponentsFactory` / `TranscriptionComponentsFactory` | 每运行新建 recognizer/VAD |
| `Recognition` | `RecognitionResult` / `SegmentRecognition` | Core 自有 DTO |
| `Recognition` | `IRecognizer` / `SherpaRecognizer` | Paraformer 封装 |
| `Subtitles` | `SrtFormatter` / `PlainTextFormatter` / `TimestampMath` | 格式化与时间戳（纯逻辑） |

**STTmini.App**（`src/STTmini.App/`，TFM net10.0，Avalonia 12.1）

| 命名空间 | 类型 | 职责 |
|----------|------|------|
| (root) | `Program` / `App` / `ViewLocator` | 入口、DI 装配、VM→View 映射 |
| `ViewModels` | `ViewModelBase` / `MainWindowViewModel` / `SettingsViewModel` | MVVM（CommunityToolkit.Mvvm 源生成器） |
| `Views` | `MainWindow` / `SettingsView` | Avalonia 视图（简体中文） |
| `Services` | `IFilePickerService` / `FilePickerService` | 文件选择/保存（StorageProvider） |
| `Converters` | `OutputFormatNameConverter` | 枚举→中文名（ComboBox） |

**STTmini.Core.Tests**（`src/STTmini.Core.Tests/`，xunit）：44 个测试覆盖全部纯逻辑 + 流水线编排（mock 组件）。

### 14.2 实现期对本文档的技术修正

实现过程中通过反射 sherpa-onnx 1.13.4 托管 DLL，修正了本文件早先的若干 API 假设，已回写至 §2.1 / §4.1 / §5.1：

- `OfflineParaformerModelConfig` **仅有** `.Model`；`Tokens` 在 `OfflineModelConfig.Tokens`。
- sherpa-onnx 原生 `SpeechSegment.Start` 是 **int 样本偏移**（非秒），封装层换算。
- 离线结果 `OfflineRecognizerResult` **无** `.Json`（仅在线有）。
- Silero VAD `MaxSpeechDuration` 默认 5s 会自动切分；v1 显式设 30s，统一由 25s `SegmentChunker` 切分。
- Avalonia 选定为 **12.1.x**（用户确认），调试可视化器已并入核心包，不再单独引用 `Avalonia.Diagnostics`。

### 14.3 待办（手动冒烟，发布前）

- 下载真实模型到 `models/`（`scripts/models.sh`），填入 SHA256 占位。
- 用真实中文视频跑一次端到端转录，核对 SRT 时间戳与纯文本段落分隔。
- 跨平台验证：Windows 单文件夹运行 + Linux tarball 运行。

---


*本文档为 STTmini 的实现基准。如需变更任何决策，先修订本文档相应章节，再调整代码。*

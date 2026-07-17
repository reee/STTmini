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
- **单文件 / 批量双模式**：主窗 header 分段切换 `[单文件|批量]`。单文件流程保留实时预览 + 双保存；批量模式支持选文件/文件夹、勾选导出格式（txt/srt/两者）、顺序转录、失败跳过继续、同目录自动产出（§4.5 / §6.2）。

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
   │                  注意1：Silero VAD 默认 MaxSpeechDuration=5s 会自动切分；
   │                  v1 把它显式设为 30s，让超长段统一由 [3] 的 25s 窗口处理（单一切分策略）。
   │                  注意2：sherpa-onnx 的 VoiceActivityDetector 是流式 API，必须按
   │                  WindowSize(512 样本) 逐块 AcceptWaveform；一次性喂入整段音频会让
   │                  内部 circular-buffer 溢出、仅保留尾部（实测 672s 输入只剩末尾 0.3s）。
   │                  切片由纯逻辑 VadWindowSlicer 负责（见 §4.2）。
   │
   ▼
[3] 超长段重切     若 segment 时长 > 25s → 固定窗口切为多个子段
   │
   ▼
[4] ASR 识别       子段按 BatchSize=8 分批送入 OfflineRecognizer
   │                  每批一次 Decode(IEnumerable<OfflineStream>)，intra-op 多线程
   │                  每段得到 Result.Text / Result.Tokens / Result.Timestamps
   │                  （并行策略见 §4.4；批内 padding 到最长段，结果逐字同单段路径）
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
| VAD | `VadWindowSlicer`（按 512 样本窗口切分喂入） | `IVoiceActivityDetector`（封装 `SherpaOnnx.VoiceActivityDetector`） |
| ASR | — | `IRecognizer`（封装 `SherpaOnnx.OfflineRecognizer`） |
| 段切分 | `SegmentChunker`（25s 重切） | — |
| 时间戳 | `TimestampMath`（偏移、cue 边界） | — |
| SRT 格式化 | `SrtFormatter` | — |
| 纯文本格式化 | `PlainTextFormatter` | — |
| 模型路径 | `ModelPathResolver` | — |
| 配置 | `Settings`（POCO）+ `SettingsStore` | — |
| 批量输入展开 | `BatchInputCollector`（混合路径→去重媒体文件列表，§4.5） | — |
| 批量输出路径 | `BatchOutputResolver`（同目录同 basename 换扩展名，§4.5） | — |
| 批量编排 | — | `ITranscriptionEngine`（seam：让 runner 可注入 stub 引擎测，§4.3 / §4.5） |

### 4.3 接口设计原则（配合 Q8 GPU seam 与 Q17 可测性）

原生类（`OfflineRecognizer`、`VoiceActivityDetector`）一律包在接口后面。理由：

1. **可测性**：纯逻辑测试通过 mock 接口完成，不依赖真实模型/原生库。
2. **Provider seam**：v1 仅 CPU；未来加 GPU 时，只需新增一个 `IRecognizer` 实现，不破坏调用方。

接口返回的 DTO（如 `RecognitionResult { Text, Tokens[], Timestamps[] }`）由 Core 自有，**不**把 sherpa-onnx 的结构体泄露到上层。

### 4.4 认识器生命周期与并行策略

- `OfflineRecognizer` **每次运行新建**，运行结束 `Dispose`。
- **不跨并发调用共享**（sherpa-onnx 的 recognizer 非线程安全，且 native 运行时初始化慢）。
- 单次运行内：一个 worker task，**单 recognizer 实例**贯穿整次转录（不每段新建）。

#### 并行策略（v1，吃满多核）

CPU 利用率优化采用**两层并行，共用同一 recognizer**，而非应用层多 recognizer 并行：

1. **intra-op 多线程（保底收益）**：`OfflineModelConfig.NumThreads` = `min(Environment.ProcessorCount, 16)`。
   ONNX Runtime 的 intra-op 线程池在单次 `Decode` 内部做大 GEMM 并行。paraformer-zh int8 是非自回归模型，intra-op 扩展性好——这一项单独就能让单次识别用满多核。16 为防服务器核数过大的保守上限，桌面用户不受影响。
2. **原生 batch 解码（叠加收益）**：把超长段重切后的子段按 `BatchSize = 8` 分组，每批先创建多个 `OfflineStream` 并 `AcceptWaveform`，再一次调用 `OfflineRecognizer.Decode(IEnumerable<OfflineStream>)`（1.13.4 已确认存在该重载）。paraformer 支持批维，批内 padding 到最长段；VAD 段长天然相近，padding 浪费有界。
   即便原生 batch 内部退化为顺序解码，也不劣于方案 A（仍是 intra-op 多线程），方案 B 为纯 upside。

**关键不变量**：批结果按 stream 创建顺序读取，段顺序、`previousSegmentEnd` 计算、纯文本段落分隔（§5.3 依赖段间顺序的 silenceBefore）全部保持。取消粒度从段边界降为批边界（每 ≤8 段一次 `ThrowIfCancellationRequested`）。进度仍逐段上报（§6.3 实时填充平滑度不退化）。

**明确不采用的方案**：应用层并行 + 多 recognizer（`Parallel.ForEach` + N 个 recognizer 实例）。理由：① recognizer 非线程安全、native init 慢；② N 份模型常驻内存 ~233MB × N，违背 Mini（§1.1 / §9）；③ 多 recognizer 各持 intra-op 线程池会产生线程过订阅，反而降吞吐。

### 4.5 批量编排（BatchTranscriptionRunner）

批量模式在 §4.1 单文件流水线之上叠一层顺序编排，**不引入新并行维度**（§4.4 并行结论不变）。

- **顺序执行**：`BatchTranscriptionRunner.RunAsync` 顺序循环每个文件，复用同一 `ITranscriptionEngine.TranscribeAsync`。**不做跨文件并行**——理由同 §4.4 否决方案（recognizer 非线程安全 + 内存×N + 线程过订阅）。单文件内部仍吃满 intra-op + batch decode。
- **per-file recognizer 生命周期**：每个文件经引擎时新建/释放 recognizer/VAD（§4.4 单 worker per run 不变）。接受 N× 初始化开销换取零并发风险——批量场景吞吐瓶颈是识别本身，非初始化。
- **失败跳过继续**：单文件抛异常（ffmpeg 报错、无音频、模型问题等）→ 记录 `BatchFileOutcome.Failed`、继续下一文件，不中止批量。异常类型映射为简短 UI 文案（与 §11.1 单文件口径一致：`FfmpegNotFoundException`→「未找到 ffmpeg」等）。结束给汇总（N 成功 / M 失败）。
- **进度两层**：内层 `TranscriptionProgress`（段 j/M）经 runner 转译为外层 `BatchTranscriptionProgress`（文件 i/N + 文件名 + 当前阶段 + 段进度 + 可选 `JustCompleted`）。UI 顶行「批量转录中…（文件 3/10：v3.mp4）」、次行「识别中…（段 5/12）」、整体进度条按「(已完成文件数 + 当前文件段进度)/总文件数」加权。
- **`JustCompleted` 信号**：每个文件边界（成功或失败）runner 上抛一次带 `BatchFileOutcome` 的进度，驱动 UI 列表行状态切换（等待→进行中→完成/失败 + 输出文件名摘要 + 错误说明）。
- **取消**：批量专用 `CancellationTokenSource`（与单文件 `_cts` 隔离），循环边界 `ThrowIfCancellationRequested`，取消冒泡到 `RunAsync` 调用方（已完成文件结局保留）。UI 恢复「取消」按钮（§6.4）。
- **输出策略**：固定「各输入文件同目录、同 basename、换扩展名」（`video.mp4` → `video.txt` / `video.srt`），由纯逻辑 `BatchOutputResolver` 解析。覆盖已存在文件（对齐 §5.4 ffmpeg `-y`）。**不支持**自定义输出目录（v1）。
- **格式选择**：`BatchOutputFormat` flags（`Txt`/`Srt`/`Both`），UI 两个 checkbox（默认双勾）。至少勾一个才能开始批量；runner 对 `None` 抛 `ArgumentException`。
- **输入展开**：`BatchInputCollector.Collect`（纯逻辑，§4.2 Audio 模块）把混合路径（文件 + 文件夹）展开为去重的媒体文件全路径列表，按路径字典序稳定排序。文件夹仅扫顶层（**不递归**子目录，v1）。扩展名白名单与 §6.2 单文件 picker 一致（`mp4/mkv/mov/avi/webm/mp3/wav/m4a/flac/aac`）。

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
  → result.Text        : string   全文（**无标点**，见下方说明）
  → result.Tokens      : string[] 每 token
  → result.Timestamps  : float[]  每 token 时间戳（秒，段内相对）
  → result.Durations   : float[]  每 token 时长（秒，可能为 null）
```

> **批量解码 API（§4.4 方案 B，吞吐优化）**：`OfflineRecognizer` 另有重载 `Decode(IEnumerable<OfflineStream> streams)`——
> 批量建 stream → 批量 `AcceptWaveform` → 一次 `Decode(IEnumerable)` → 按序读各 `stream.Result`。
> 每批 `BatchSize = 8` 段，批内 padding 到最长段。批结果顺序由 stream 创建顺序决定，与单段路径逐字一致。

> 配置要点（v1.13.4 实测 API）：
> - `config.ModelConfig.Paraformer.Model` = `model.int8.onnx` 路径（`OfflineParaformerModelConfig` **仅有** `.Model` 字段）。
> - `config.ModelConfig.Tokens` = `tokens.txt` 路径（Tokens 在 `OfflineModelConfig` 上，**不在** Paraformer 子配置上）。
> - `config.ModelConfig.NumThreads` = `min(Environment.ProcessorCount, 16)`（§4.4 intra-op 多线程）。值由 `ITranscriptionComponentsFactory` 在构造 `SherpaRecognizer` 时传入，类内不设默认。
> - 离线结果类型 `OfflineRecognizerResult` **没有** `.Json` 属性（`.Json` 仅存在于在线结果）。

- **全局时间戳** = `seg.StartSeconds + result.Timestamps[i]`（VAD 段）或 `子段全局起点 + result.Timestamps[i]`（超长段重切后）。
  - sherpa-onnx 原生 `SpeechSegment.Start` 是 **int 样本偏移**（非秒）；封装层将其换算为秒 `Start / 16000f` 后填入 Core 自有的 `SpeechSegment.StartSeconds`。
- Paraformer-zh int8 实测**不输出任何标点**（无句号/逗号/问号）。早先文档称「原生输出中文标点」与实测不符，已修订。`OfflineParaformerModelConfig` 仅有 `.Model` 字段、配置层无标点开关；该 int8 模型本身就是无标点输出。
  - **影响**：纯文本段落分隔只能依赖 VAD 段间静音（§5.3），无句号可作切分信号；段内为一长行无标点文本（当前设计接受，见 §5.3）。
  - **不**引入额外标点模型（sherpa-onnx 的 `OfflinePunctuation`/CT-Transformer ~400MB，违背 Mini，§9.2）。若未来需要标点，再单独立项评估。

### 5.2 SRT 格式化规则

- **单 VAD 段 → 一个 SRT cue**（超长段重切后，每个子段 → 一个 cue）。
- **cue 边界**：取段内首 token 时间戳为 cue 起点、末 token 时间戳为终点，加上段全局偏移。**不**用 VAD 段边界。
- **时间格式**：`HH:MM:SS,mmm`（逗号分隔毫秒）。
- **超长段处理**：段时长 > 25s 且无内部静音 → 固定 25s 窗口切分子段；每个子段独立识别，独立成 cue。
- **v1 仅支持 SRT**，不支持 VTT（未来可加，数据相同仅分隔符不同）。

### 5.3 纯文本格式化规则

- 按段顺序拼接各段 `Result.Text`。
- **段落分隔启发式**：相邻两段之间的静音间隔 > **0.6 秒** → 插入空行（段落分隔）；否则单换行。
  - 取值依据：实测快语速中文视频句间停顿中位数约 0.47s、p75 约 0.67s（如「反向旅游」素材：31 段 gap 中最大 0.95s，**无任何 gap ≥ 1.0s**）。早先的 2s 阈值在此类视频上永远触达不到，导致纯文本全部兜底为单换行。0.6s 取 p75 附近，能切出段落又不至于把句内停顿误判为段落断点。
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

主窗 header `[单文件|批量]` 分段切换决定走哪条工作流（§4.5）。两套流程在 VM 中字段/CTS 完全隔离（单文件 `_cts` + `IsBusy`；批量 `_batchCts` + `IsBatchBusy`），切换时若任一在跑则禁用切换。

**单文件**（默认）。主流程：

1. 选择输入文件（文件选择对话框 + 拖放，按钮文案「浏览…」）。
2. 点击转录（进度反馈，§6.3）。
3. 内联查看纯文本预览结果（实时填充）。
4. 保存为 `.txt`（「保存文本」）或 `.srt`（「保存字幕」）——两种格式从同一份段数据即时格式化，**无需重跑识别**（引擎结果 `TranscriptionResult.Segments` 携带全部段，§5.1）。

> 早先版本让用户在 UI 上选「纯文本 / SRT」二选一，并据此保存单格式。现已改为：一次转录即同时持有两种表示，主窗结果区固定展示纯文本（可读性最好），SRT 经「保存字幕」按钮按段即时格式化写出。设置页的「默认输出格式」随之移除（§6.5 / §8.2）。

**批量**（§4.5）。主流程：

1. 选择文件（多选）或文件夹（顶层展开，不递归），或直接拖入多个文件/文件夹。`BatchInputCollector` 去重 + 扩展名白名单过滤后填入文件列表（每行 `BatchItemViewModel`：文件名 + 状态指示）。
2. 勾选导出格式（`.txt` / `.srt` / 两者；默认两者都勾）。
3. 点「开始批量转录」→ `BatchTranscriptionRunner` 顺序调用引擎，每行实时刷新状态（等待→进行中→✓ 已完成 / ✕ 失败），整体进度条两层（文件 i/N + 段 j/M）。
4. 输出**自动写盘到各输入文件同目录**（`video.mp4` → `video.txt` / `video.srt`），无需逐文件保存对话框。
5. 失败文件跳过继续，结束给汇总（N 成功 / M 失败）。可随时点「取消」中止（§6.4）。

### 6.3 进度反馈

分阶段真实进度（非不确定进度条），每阶段有中文标签：

- `解码音频…`
- `语音活动检测…`
- `识别中…（段 i / 总 N）`

ASR 阶段每完成一段即通过 `IProgress<T>` 推送一次，结果面板**实时填充**（不等全部完成）。

### 6.4 取消（单文件已移除；批量已恢复）

- **单文件模式取消能力已移除**：取消按钮、`CancelCommand`、`CanCancel` 均删除。
- 取舍依据：实测短/中长内容转录耗时可控（CPU int8 paraformer），且 action-bar 常驻取消按钮在空闲态造成视觉冗余。移除换简洁。
- 流水线内部仍保留 `CancellationTokenSource` 管线（`TranscribeAsync` 内 `new CancellationTokenSource()` + `_cts.Token` 传给 engine + `_cts.Dispose()`）——这是 §7 线程模型的合理基建，与 UI 取消能力解耦。
- **批量模式恢复取消按钮**（`CancelBatchCommand`，批量 action-bar 中间）：批量是长任务（N 个文件累加，可达数小时），无取消会卡死。批量专用 `_batchCts` 与单文件 `_cts` 隔离；取消在 `BatchTranscriptionRunner` 循环边界生效，已完成文件结局保留。
- 早期版本：取消按钮转录中可用，段边界粒度（当前段识别完成后停止），取消后已识别段保留显示。

### 6.5 Settings 页

| 项 | 说明 |
|----|------|
| ffmpeg 路径 | 自动检测 PATH，用户可手动覆盖。未配置时转录入口给出明显提示。 |

> 模型目录随发布包附带（§8.3 / §9.1），用户不可改、无需在 UI 暴露——早期文档在此列过只读展示项，现已移除（冗余）。
> 「默认输出格式」亦已移除——转录结果同时持有纯文本与 SRT，由主窗双保存按钮分别导出（§6.2）。

### 6.6 视觉设计语言（B 方案：卡片现代）

主窗与设置弹窗采用统一的**卡片现代风**（Linear/Notion 系），由集中式样式层 `Styles/AppTheme.axaml` 定义：

- **配色**：浅灰页面底（`#F4F5F7`）+ 白卡片 + 柔阴影；靛蓝强调色（`#5B5BD6`）用于主按钮/进度条/链接。
- **结构（主窗）**：**无顶部应用栏**——单张居中主卡片，卡片内由 `Grid RowDefinitions="Auto,Auto,Auto,*,Auto"` 锁骨架（**不**再用 StackPanel + 外层 ScrollViewer，否则结果文本会把底 action-bar 撑出屏幕）：
  - R0 卡片 header 行：左 logo + STTmini 小标题，**中右 `[单文件|批量]` 分段切换**（`Border.segmented` + 两个 `RadioButton.segmented-item` 绑定 `IsBatchMode`，§4.5 / §6.2），右设置齿轮 `Button.icon-btn`（`OpenSettings`）。早期版本有独立顶栏 appbar 承载主 CTA；CTA 移入卡片后 appbar 失去存在理由已移除。
  - R1 输入段（`card-section`）：单文件模式 = input-pill + 浏览 + **开始转录 CTA 同行**（浏览→转录是相邻步骤，CTA 紧跟输入框；CTA 禁用判据 `CanTranscribe => !IsBusy && _inputPath 非空 && IsFfmpegAvailable`，三前置条件任一不满足即灰显）。ffmpeg 不可用时输入段下方显示「未检测到 ffmpeg，点右上角 ⚙ 设置路径」。批量模式 = 选文件/选文件夹 + 格式 checkbox（txt/srt）；批量列表的「移除全部」入口下移到 R3 列表头（与「清空已完成」并列、词汇区分）。**R1/R3/R4 各段用 `IsVisible={IsBatchMode}` 按 `IsBatchMode` 切换单文件/批量子视图**，骨架不变。**支持拖放——`DragDrop.AllowDrop` 挂在最外层卡片上而非 input-pill**，命中区扩大到整张卡片；单文件模式取首个 dropped 文件，批量模式枚举全部（含文件夹，由 `BatchInputCollector` 展开）。
  - R2 进度段（`card-section`，`IsVisible={IsBusy}`，Auto 行隐藏即坍缩）。批量模式另有独立进度段 `IsVisible={IsBatchBusy}`，两层进度（顶行文件 i/N + 次行段 j/M + 整体加权进度条）。
  - R3 结果段（`card-section`，`*` 行驱动高度）：单文件模式 = result-panel 内 `TextBox.result-text` 加 `ScrollViewer.VerticalScrollBarVisibility="Auto"`；批量模式 = `Border.batch-list` 内 `ListBox`，**顶部列表头**（左 `BatchItemsCountText`「N 个文件」+ 右两按钮「清空已完成」（仅 `HasCompletedItems` 时可见）/「移除全部」（仅 `HasBatchItems` 时可见，破坏性更强放最右），**空态虚线拖放区** `Border.drop-zone`（📁 + 引导文案，列表为空时撑满 `*` 行），**行模板** `[状态圆点] [文件名 + 状态/产出/错误 + 运行中行内 2px 进度条] [打开/重试] [×]`（每行常驻 × 移除按钮，运行中禁用；成功行「打开」用系统默认程序打开产出、失败行「重试」）。内部滚动。**仅内容区内部滚动**，action-bar 永远钉在卡片底——「保持 App 高度不变」的关键：骨架锁高 + 内容区独占弹性。行操作经 `BatchItemViewModel` 上的 `RemoveRequested`/`OpenOutputRequested`/`RetryRequested` 回调注入父 VM（item 不反向持有 parent）。
  - R4 action-bar：单文件模式 = 状态 + 保存文本 + 保存字幕（取消已移除，§6.4）。批量模式 = 批量状态 + **取消** + 开始批量转录（批量恢复取消，§6.4）。
- **结构（设置弹窗）**：单卡片段（仅 ffmpeg 路径，§6.5）。弹窗高 ~390px、`CanResize=False`、**无外层 ScrollViewer**（只剩一段不需要滚）。早期为「三段内容」设计的高弹窗（520px）在设置项收敛后留有大量空白，已压低高度。仍保留 appbar + 单卡片 + action-bar 的视觉骨架以与主窗统一。
- **样式机制**：Avalonia 12 class 选择器（`Classes="card"` / `"card-header"` / `"primary"` / `"input-pill"` 等）。`App.axaml` 的 `Application.Styles` 里**必须先放 `<FluentTheme />`，再 `<StyleInclude>` 本主题**——`AppTheme` 只定义 class 选择器覆盖，控件模板（ComboBox 弹出、TextBox 文字呈现等）全靠 FluentTheme 提供；漏掉 FluentTheme 会导致 ComboBox 点不开下拉、TextBox 渲染空白。`AppTheme.axaml` 以 `AvaloniaResource` 打包进 `.csproj`。
- **logo**：卡片 header 行 STTmini 前的 24×24 图标 = 真实 app icon，以 `Assets/logo.png` 加载（`<Image Source="avares://STTmini.App/Assets/logo.png" />`，样式 `Image.logo`）。早期用纯 AXAML 渐变方块占位，现已替换。PNG 走 Avalonia `<Image>` 解码路径（比 ICO 稳），与 app icon 同源（同由 `scripts/generate_icon.py` 渲染）。
- **应用图标**（`src/STTmini.App/Assets/app.ico`，7 档多分辨率 16/24/32/48/64/128/256）：蓝盘（`#4285F4`）+ 白色播放三角 + 6 条字幕条的"语音→字幕"隐喻图。三角形与字幕条整体以 0.88 缩放因子绕盘心收缩，保持居中。两处使用同一文件：
  - `.csproj` 的 `<ApplicationIcon>` → 嵌入 exe 的 Win32 `RT_ICON` 资源（Explorer / 任务栏 / Alt+Tab / 发布包图标显示）。
  - `<AvaloniaResource Include="Assets\app.ico" />` → 作为 `avares://STTmini.App/Assets/app.ico` 资源，供 `MainWindow` 的 `Icon` 属性加载（窗口左上角图标）。
  - 源图由 `scripts/generate_icon.py` 用 Pillow（4× 超采样 + LANCZOS 降采样）按几何重渲染生成，同时导出 `app.ico`（多尺寸）与 `logo.png`（256×256），无需 cairo/svg 原生依赖；设计改动后重跑该脚本即可。`Assets/` 下的 `.ico`/`.png` 为生成产物，进 Git 以便无 Python 环境也能构建。
- 视觉来源：`prototype/ui-redesign/`（throwaway HTML 原型，变体 B 胜出，A 极简/C 深色落选）。

---

## 7. 线程模型

- UI 线程：Avalonia 主线程，不执行任何 CPU 密集工作。
- 转录流水线：单次 `Task.Run`，跑在线程池。
- 进度回传：`IProgress<T>`（Avalonia 自动 marshal 到 UI 线程）。
- 取消：`CancellationToken`，传入 worker；在批循环边界检查 `ThrowIfCancellationRequested()`（每 ≤ BatchSize 段一次，§4.4 / §6.4）。
- 约束：**单 worker per run**，recognizer per-run 新建与释放（不跨并发调用共享）。

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
- `LastInputDirectory`（string?）

**不**记录最近文件列表、不记录窗口几何。
> 旧版曾含 `DefaultOutputFormat`——转录结果现同时持有纯文本与 SRT（§6.2），该项已移除。`SettingsStore` 用 System.Text.Json 默认 `Skip` 未映射成员，残留旧键会被静默忽略，无需迁移。
> 批量模式（§4.5）**不新增 Settings 字段**：输出固定写各输入文件同目录（无需配置输出目录）、格式勾选与模式选择是会话级 UI 状态（默认双勾，不持久化）。

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

> **Windows 原生等价脚本**：`scripts/publish.ps1` + `scripts/models.ps1`（PowerShell）是上述 bash 脚本的对等实现，供 Windows 用户不装 Git Bash 即可本地发布（CI 仍走 `publish.sh`）。两者调用同一份 `dotnet publish` 命令（§10.2 硬约束完全一致），仅打包工具不同：PowerShell 版用 .NET `ZipFile` 打 zip、用 Windows 内置 bsdtar（`%WINDIR%\System32\tar.exe`）打 tar.gz——**不**用 PATH 里的 `tar`，因为 Git for Windows 的 GNU tar 会把 `D:\path` 误解析为远程主机语法。

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
| `Audio` | `VadWindowSlicer` | VAD 喂入的 512 样本窗口切片（纯逻辑） |
| `Audio` | `SpeechSegment` / `IVoiceActivityDetector` / `SherpaVoiceActivityDetector` | VAD 抽象 + Silero 实现 |
| `Audio` | `IAudioExtractor` | 音频提取接口 |
| `Audio` | `BatchInputCollector` | 批量混合路径→去重媒体文件列表（纯逻辑，§4.5） |
| `Configuration` | `Settings` | 设置 POCO（§8.2） |
| `Configuration` | `SettingsStore` | 配置读写（损坏回退默认） |
| `Configuration` | `AppPaths` | 平台相关路径（portable/XDG） |
| `Errors` | `STTminiException` 及四个子类 | 异常分类（§11.1） |
| `Logging` | `FileLoggerProvider` | 手写文件 logger（含滚动） |
| `Models` | `ModelPathResolver` / `ModelFileNames` | 模型路径解析与存在性校验 |
| `Pipeline` | `ITranscriptionEngine` / `TranscriptionEngine` | 转录引擎接口（批量 seam）+ 实现（流水线编排 §4.1 / §4.5 / §7） |
| `Pipeline` | `TranscriptionProgress` / `TranscriptionStage` | 进度报告 |
| `Pipeline` | `ITranscriptionComponentsFactory` / `TranscriptionComponentsFactory` | 每运行新建 recognizer/VAD |
| `Pipeline` | `BatchTranscriptionRunner` / `IBatchOutputWriter` / `FileBatchOutputWriter` | 批量顺序编排（§4.5：失败跳过、两层进度、写输出） |
| `Pipeline` | `BatchInputCollector`→见 Audio；`BatchOutputResolver` / `BatchOutputFormat` | 批量输出路径解析（同目录同 basename 换扩展名）+ 格式 flags |
| `Pipeline` | `BatchTranscriptionProgress` / `BatchFileOutcome` / `BatchTranscriptionResult` | 批量进度 + 单文件结局 + 总结果 DTO |
| `Recognition` | `RecognitionResult` / `SegmentRecognition` | Core 自有 DTO |
| `Recognition` | `IRecognizer` / `SherpaRecognizer` | Paraformer 封装 |
| `Subtitles` | `SrtFormatter` / `PlainTextFormatter` / `TimestampMath` | 格式化与时间戳（纯逻辑） |

**STTmini.App**（`src/STTmini.App/`，TFM net10.0，Avalonia 12.1）

| 命名空间 | 类型 | 职责 |
|----------|------|------|
| (root) | `Program` / `App` / `ViewLocator` | 入口、DI 装配、VM→View 映射 |
| `ViewModels` | `ViewModelBase` / `MainWindowViewModel` / `SettingsViewModel` | MVVM（CommunityToolkit.Mvvm 源生成器）。`MainWindowViewModel` 内嵌两套隔离字段：单文件（`_inputPath`/`_cts`/`IsBusy`）与批量（`BatchItems`/`_batchCts`/`IsBatchBusy`，§4.5 / §6.2） |
| `ViewModels` | `BatchItemViewModel` / `BatchStatusToBrushConverter` | 批量列表行 VM（文件名+状态+行内进度+产出/错误摘要+三个操作回调 `RemoveRequested`/`OpenOutputRequested`/`RetryRequested`）+ 状态→颜色转换器（§4.5 / §6.3 / §6.6） |
| `Views` | `MainWindow` / `SettingsView` | Avalonia 视图（简体中文） |
| `Services` | `IFilePickerService` / `FilePickerService` | 文件选择/保存（StorageProvider） |
| `Services` | `IFileLauncher` / `FileLauncher` | 用系统默认程序打开产出文件/目录（封装 `LauncherExtensions`，批量行「打开」操作，§6.6） |
| `Styles` | `AppTheme.axaml`（loose `<Styles>` 资源，无 code-behind） | 集中式样式层（class 选择器设计系统，B 方案，§6.6） |
| `Assets` | `app.ico`（7 档多分辨率）+ `logo.png`（256×256） | 应用图标：`app.ico` 由 `<ApplicationIcon>` 嵌入 exe + `AvaloniaResource` 供 `MainWindow.Icon`；`logo.png` 供顶栏 `<Image>` logo（§6.6）。两者同源，源图脚本 `scripts/generate_icon.py` |

**STTmini.Core.Tests**（`src/STTmini.Core.Tests/`，xunit）：覆盖全部纯逻辑 + 流水线编排（mock 组件）。具体用例数随实现增长，以 `dotnet test` 实测为准。

### 14.2 实现期对本文档的技术修正

实现过程中通过反射 sherpa-onnx 1.13.4 托管 DLL，修正了本文件早先的若干 API 假设，已回写至 §2.1 / §4.1 / §5.1：

- `OfflineParaformerModelConfig` **仅有** `.Model`；`Tokens` 在 `OfflineModelConfig.Tokens`。
- sherpa-onnx 原生 `SpeechSegment.Start` 是 **int 样本偏移**（非秒），封装层换算。
- 离线结果 `OfflineRecognizerResult` **无** `.Json`（仅在线有）。
- Silero VAD `MaxSpeechDuration` 默认 5s 会自动切分；v1 显式设 30s，统一由 25s `SegmentChunker` 切分。
- Avalonia 选定为 **12.1.x**（用户确认），调试可视化器已并入核心包，不再单独引用 `Avalonia.Diagnostics`。
- 引入集中式样式层 `Styles/AppTheme.axaml`（B 方案卡片现代风，§6.6）：早先视图样式全内联；v1 改为 class 选择器设计系统，主窗与设置弹窗共享。`App.axaml` 用 `<StyleInclude>` 合并，`.csproj` 以 `AvaloniaResource` 打包。视觉源自 throwaway HTML 原型 `prototype/ui-redesign/`（变体 B 胜出）。
- **体积优化（平台后端细粒度引用）**：早先 `STTmini.App.csproj` 引聚合包 `Avalonia.Desktop`——它传递拉入 `Avalonia.Win32` + `Avalonia.X11` + `Avalonia.Native`（macOS）+ `Avalonia.Skia` + `Avalonia.HarfBuzz`，导致 win-x64 发布产物里也打包了 X11/Native/FreeDesktop/DBus 等无关平台后端。改为按 RID 精确引用单一平台后端：`win-x64` → `Avalonia.Win32` + `Avalonia.Skia` + `Avalonia.HarfBuzz`（注意 `Avalonia.Win32` 的 nuspec 不传递 Skia，需显式补；Linux 端 `Avalonia.X11` 已传递依赖 Skia，无需补）；设计期（无 RID 的 `dotnet build` / IDE）保留 `Avalonia.Desktop` 兜底。
  - **副作用与对策**：换细粒度包后 `UsePlatformDetect()` 不再可用（它由 `Avalonia.Desktop` 提供）。`Program.cs` 改用 csproj 按 RID 定义的 `WINDOWS` / `LINUX` 编译符号分叉，显式调对应后端注册：`UseWin32().UseSkia().UseHarfBuzz()`（Windows）/ `UseX11().UseSkia().UseHarfBuzz()`（Linux）。注意 `UsePlatformDetect` 原本会自动配 Skia + HarfBuzz，手动调用时两者**必须显式补全**，否则启动报 "No rendering/text shaping system configured"。无 RID 兜底分支仍调 `UsePlatformDetect`。
  - `UseWin32` / `UseX11` / `UseSkia` / `UseHarfBuzz` 均为扩展方法，所在命名空间是根 `Avalonia`（尽管 `UseWin32` 的实现类位于 `Avalonia.Win32.dll`），无需额外 `using`。
- **体积优化（移除内嵌 Inter 字体）**：早先引 `Avalonia.Fonts.Inter`（~1.9MB）并在 `Program.cs` 调 `.WithInterFont()`。但 Inter 仅含西文字形，本应用 UI 为简体中文（§6.1），中文最终走系统字体 fallback。改为移除该包与调用，UI 西文跟随系统默认字体（Windows=Segoe UI / Linux=DejaVu Sans，两平台均含中文），视觉差异极小。
- **吞吐优化（CPU 多核并行）**：首版 `NumThreads=1` + 串行逐段 `Decode(stream)`，N 核机器稳态 CPU≈1/N（如 8 核 ~12%）。改为两层并行共用单 recognizer（§4.4）：① `NumThreads=min(ProcessorCount,16)` 走 ONNX Runtime intra-op；② `TranscriptionEngine` 按 `BatchSize=8` 分批，每批一次 `Decode(IEnumerable<OfflineStream>)`（1.13.4 反射确认存在该重载，paraformer 支持批维）。`IRecognizer` 新增默认接口方法 `RecognizeMany`（回退为循环 `Recognize`，保持测试 stub 零改动）。批结果按 stream 创建顺序读取，与单段路径逐字一致；取消粒度降为批边界；进度仍逐段上报（§6.3 平滑度不退化）。明确**不**采用应用层多 recognizer 并行（线程不安全 + 内存×N + 线程过订阅）。
- **批量模式新增（§4.5 / §6.2 / §6.4）**：
  - 从 `TranscriptionEngine` 抽接口 `ITranscriptionEngine`（仅 `TranscribeAsync`，§4.3 seam 精神），DI 注册由具体类改为 `AddSingleton<ITranscriptionEngine, TranscriptionEngine>()`，依赖方（`MainWindowViewModel`）改注入接口。零行为回归，现有 `TranscriptionEngineTests` 仍直接构造具体类不受影响。
  - `BatchTranscriptionRunner`（新）顺序调用引擎 N 次，失败跳过继续（异常类型映射为简短 UI 文案），按 `BatchOutputFormat` flags 写输出。写盘副作用经 `IBatchOutputWriter` 抽象隔离，便于测试注入内存采集器。**明确不并行跨文件**（同 §4.4 否决理由）。
  - 纯逻辑 `BatchInputCollector`（混合路径→去重媒体文件列表，文件夹仅顶层不递归）+ `BatchOutputResolver`（同目录同 basename 换扩展名）+ `BatchOutputFormat` flags。均覆盖单测。
  - UI：主窗 header 加 `[单文件|批量]` 分段切换（`Border.segmented` + `RadioButton.segmented-item`，绑定 `IsBatchMode`）；R1/R3/R4 各段用 `IsVisible` 切换单文件/批量子视图，骨架 Grid 不变；批量列表 `ListBox` + `BatchItemViewModel` 行 VM + `BatchStatusToBrushConverter` 状态圆点配色。拖放扩展：单文件模式取首个 dropped 文件，批量模式枚举全部（`DataTransferExtensions.TryGetFiles`，含文件夹由 `BatchInputCollector` 展开）。
  - `IFilePickerService` 增 `PickOpenFilesAsync`（`AllowMultiple=true`）+ `PickFolderAsync`（`OpenFolderPickerAsync`）。`AppTheme.axaml` 增 `segmented`/`segmented-item`/`batch-list`/`batch-empty`/`status-dot` 样式类 + `SuccessBrush`/`PendingBrush`/`RunningBrush` 色板 token。
  - `RadioButton` 在 Avalonia 12 **不直接支持 `BoxShadow`**（仅 `Border` 有）——分段选中态只改背景色 + 文字色，去掉阴影 setter。
  - `BatchStatusToBrushConverter` 从应用级 `IResourceHost`（`Application.Current`）按 token 名取色板，避免色值在代码里重复。
- **批量列表 UX 优化（§6.6）**：行操作（移除 / 打开产出 / 重试）经 `BatchItemViewModel` 上三个回调字段（`RemoveRequested`/`OpenOutputRequested`/`RetryRequested`）注入父 VM，item 不反向持有 parent，避免循环引用耦合（命令参数走 CommandParameter 字符串是黑魔法，弃用）。
  - **行内 × 移除按钮常驻**（HandBrake/Adobe Media Encoder 模式，非 hover-reveal——发现性更好），运行中行禁用。**不做 checkbox 多选**（同类转录/编码工具均无，单行 × 已够）。
  - **列表头**「N 个文件」+「清空已完成」+「移除全部」（VS Code 模式），`BatchItemsCountText`/`HasCompletedItems`/`HasBatchItems` 派生属性经 `CollectionChanged` 联动刷新。「移除全部」原在 R1 输入段（文案「清空」），下移到 R3 列表头最右、文案改为「移除全部」与「清空已完成」词汇区分；破坏性更强的操作放最右（UI 惯例）。两个清空按钮均按状态条件渲染——空列表时列表头只显示计数。
  - **运行中行内 2px 细进度条**（与顶部整体进度条互补）；`OnBatchProgress` 仅在首次进入运行态调 `MarkRunning`（它重置进度），之后用 `UpdateProgress` 持续推进——否则每次进度回传都把行内条拍回 0。
  - **行解析改为按 InputPath（完成事件）/ FileName（运行中）查找**，而非按索引：失败重试是**子集运行**（`StartBatchAsync(forcedInputs: [单项])`），索引不再对齐 `BatchItems` 顺序。
  - **空态虚线拖放区**（Aiko/下载管理器模式）：📁 + 引导文案，列表为空时撑满 `*` 行。
  - 「打开产出」经新 seam `IFileLauncher`/`FileLauncher`（封装 `TopLevel.Launcher.LaunchFileInfoAsync`/`LaunchDirectoryInfoAsync`）：产出 1 个打开文件、多个打开所在目录（更稳，避免歧义）。失败静默（平台默认关联程序缺失会抛）。
  - **重试语义简化**：失败行「重试」→ `MarkPending` + 若空闲立即 `StartBatchAsync(forcedInputs:[该项])`（仅重跑该项）；若批量在跑则仅重置状态 + 提示稍后再开始。**不实现**队列插入（避免改 runner）。
  - Avalonia 12 `TopLevel.Launcher` 属性存在但无 XML 文档，反射/XML 查不到——以编译器实参为准。

### 14.3 待办（手动冒烟，发布前）

- 下载真实模型到 `models/`（`scripts/models.sh`），填入 SHA256 占位。
- 用真实中文视频跑一次端到端转录，核对 SRT 时间戳与纯文本段落分隔。
- **多核 CPU 占用核对（§4.4 吞吐优化）**：转录中观察任务管理器 CPU 占用，应在 8 核机器上达 ~70-90%（改前 ~12%）；并核对识别文本与优化前逐字一致（并行只动吞吐，不改识别内容）。若 CPU 仍低，排查是否 NumThreads cap 过低或 batch 内部未并行。
- **批量模式核对（§4.5）**：选一文件夹含多个中文视频 → 勾 txt+srt → 开始批量转录；核对：①每个文件产出 `同名.txt`+`同名.srt` 在源目录；②文件列表行状态（等待/进行中/完成/失败）实时刷新、运行中行内 2px 进度条随段推进；③故意放一个无音频/损坏文件，确认失败被跳过、其余继续、结束汇总正确；④批量中点「取消」，确认已完成文件保留、未处理的停止；⑤切回单文件模式仍正常工作。
- **批量列表 UX 核对（§6.6）**：①行右侧 × 按钮可移除等待/完成/失败行（运行中那行禁用）；②成功行「打开」按钮：产出 1 个→打开该文件、产出多个→打开所在目录；③失败行「重试」：空闲时仅重跑该项、批量中时提示稍后；④列表头「清空已完成」仅移除完成项、「移除全部」清空整个列表，两者均按状态条件渲染（空列表时不显示）；⑤列表为空时显示虚线拖放区；⑥计数「N 个文件」随增删刷新。
- 跨平台验证：Windows 单文件夹运行 + Linux tarball 运行。

---


*本文档为 STTmini 的实现基准。如需变更任何决策，先修订本文档相应章节，再调整代码。*

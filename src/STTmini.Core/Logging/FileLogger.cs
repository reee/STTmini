using Microsoft.Extensions.Logging;

namespace STTmini.Core.Logging;

/// <summary>
/// 手写极简文件 logger（AGENTS.md §2 / §8.4 / §11.2）。不引入 Serilog。
/// 单文件追加写，线程安全（lock），简单滚动（超过阈值新建序号文件）。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly string _logFileBase;
    private readonly long _maxBytesPerFile;
    private readonly LogLevel _minLevel;
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private string _currentFile = string.Empty;

    public FileLoggerProvider(string logDirectory, LogLevel minLevel = LogLevel.Information, long maxBytesPerFile = 5 * 1024 * 1024)
    {
        _logDirectory = logDirectory;
        _logFileBase = "sttmini";
        _minLevel = minLevel;
        _maxBytesPerFile = maxBytesPerFile;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal void Write(LogLevel level, string categoryName, Exception? exception, string message)
    {
        if (level < _minLevel)
        {
            return;
        }

        var levelStr = level switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERR ",
            LogLevel.Critical => "CRIT",
            _ => "????",
        };

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{levelStr}] {categoryName}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception.ToString();
        }

        lock (_gate)
        {
            try
            {
                EnsureWriter();
                _writer!.WriteLine(line);
                _writer.Flush();
                RollIfNeeded();
            }
            catch
            {
                // 日志失败不应影响主流程
            }
        }
    }

    private void EnsureWriter()
    {
        if (_writer is not null)
        {
            return;
        }

        Directory.CreateDirectory(_logDirectory);
        _currentFile = Path.Combine(_logDirectory, $"{_logFileBase}.log");
        _writer = new StreamWriter(_currentFile, append: true) { AutoFlush = false };
    }

    private void RollIfNeeded()
    {
        try
        {
            if (!File.Exists(_currentFile))
            {
                return;
            }

            var len = new FileInfo(_currentFile).Length;
            if (len < _maxBytesPerFile)
            {
                return;
            }

            _writer!.Dispose();
            _writer = null;

            // 滚动：保留最近 3 个历史文件
            for (int i = 3; i >= 1; i--)
            {
                var src = Path.Combine(_logDirectory, $"{_logFileBase}.{i}.log");
                var dst = Path.Combine(_logDirectory, $"{_logFileBase}.{i + 1}.log");
                if (File.Exists(src))
                {
                    if (i == 3 && File.Exists(dst))
                    {
                        File.Delete(dst);
                    }

                    File.Move(src, dst, overwrite: true);
                }
            }

            var archive = Path.Combine(_logDirectory, $"{_logFileBase}.1.log");
            File.Move(_currentFile, archive, overwrite: true);
        }
        catch
        {
            // 滚动失败忽略
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string categoryName, FileLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            _provider.Write(logLevel, _categoryName, exception, message);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Threading;
using CodeWF.Log.Core;

namespace ClearC.Desktop.Infrastructure.Logging;

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message);

/// <summary>
/// 装饰 <see cref="CodeWfApplicationLogger"/>：写入前统一脱敏，同时维护界面日志面板的数据源。
/// </summary>
public sealed class InMemoryLogStore : IApplicationLogger
{
    private const int DefaultMaxEntries = 1000;

    private readonly IApplicationLogger _inner;
    private readonly int _maxEntries;
    private readonly Lock _gate = new();

    public InMemoryLogStore(IApplicationLogger inner, int maxEntries = DefaultMaxEntries)
    {
        _inner = inner;
        _maxEntries = maxEntries;
    }

    /// <summary>只能在 UI 线程读取。</summary>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Information(string message) => Write("INFO", message);

    public void Warning(string message, Exception? exception = null) => Write("WARN", message, exception);

    public void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    public string ToText()
    {
        lock (_gate)
        {
            var builder = new StringBuilder();
            foreach (var entry in Entries)
            {
                builder.AppendLine($"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}");
            }

            return builder.ToString();
        }
    }

    public void Clear() => OnUi(() =>
    {
        lock (_gate)
        {
            Entries.Clear();
        }
    });

    private void Write(string level, string message, Exception? exception = null)
    {
        var redacted = PathRedactor.Redact(message);
        switch (level)
        {
            case "WARN":
                _inner.Warning(redacted, exception);
                break;
            case "ERR":
                _inner.Error(redacted, exception);
                break;
            default:
                _inner.Information(redacted);
                break;
        }

        var entry = new LogEntry(DateTimeOffset.Now, level, redacted);
        OnUi(() =>
        {
            lock (_gate)
            {
                Entries.Add(entry);
                while (Entries.Count > _maxEntries)
                {
                    Entries.RemoveAt(0);
                }
            }
        });
    }

    private static void OnUi(Action action)
    {
        // 没有 Avalonia 应用上下文（例如纯单元测试）时直接同步执行，避免事件被永远排队。
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}

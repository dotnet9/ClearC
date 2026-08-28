using CodeWF.Log.Core;

namespace ClearC.Desktop.Infrastructure.Logging;

public sealed class CodeWfApplicationLogger : IApplicationLogger
{
    public static CodeWfApplicationLogger Instance { get; } = new();

    private CodeWfApplicationLogger()
    {
    }

    public void Information(string message) => Logger.Info(message);

    public void Warning(string message, Exception? exception = null) => Logger.Warn(message, exception);

    public void Error(string message, Exception? exception = null) => Logger.Error(message, exception);
}

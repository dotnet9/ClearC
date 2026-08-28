namespace ClearC.Desktop.Infrastructure.Logging;

public sealed class NullApplicationLogger : IApplicationLogger
{
    public static NullApplicationLogger Instance { get; } = new();

    private NullApplicationLogger()
    {
    }

    public void Information(string message)
    {
    }

    public void Warning(string message, Exception? exception = null)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}

namespace ClearC.Desktop.Infrastructure.Logging;

public interface IApplicationLogger
{
    void Information(string message);
    void Warning(string message, Exception? exception = null);
    void Error(string message, Exception? exception = null);
}

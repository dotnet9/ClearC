using Avalonia.Media;

namespace ClearC.Desktop.ViewModels;

public sealed record LogEntryViewModel(string Time, string Level, string Message, IBrush LevelBrush)
{
    public static LogEntryViewModel Create(string level, string message)
    {
        IBrush brush = level switch
        {
            "OK" => Brushes.MediumSpringGreen,
            "WARN" => Brushes.Gold,
            "ERR" => Brushes.LightCoral,
            _ => new SolidColorBrush(Color.Parse("#62A9EE"))
        };
        return new(DateTime.Now.ToString("HH:mm:ss"), level, message, brush);
    }
}

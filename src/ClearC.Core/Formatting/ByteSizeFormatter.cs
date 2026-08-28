using System.Globalization;

namespace ClearC.Core.Formatting;

public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        var displayValue = (double)value;

        while (displayValue >= 1024 && unitIndex < Units.Length - 1)
        {
            displayValue /= 1024;
            unitIndex++;
        }

        var format = unitIndex switch
        {
            0 => "0",
            1 or 2 => "0.0",
            _ => "0.00"
        };

        return $"{displayValue.ToString(format, CultureInfo.InvariantCulture)} {Units[unitIndex]}";
    }
}

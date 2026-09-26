namespace ClearC.Core.Formatting;

public static class RelativeTimeFormatter
{
    /// <summary>原型里的相对时间：<c>今天 HH:mm</c> / <c>昨天 HH:mm</c> / <c>N 天前</c> / <c>—</c>。</summary>
    public static string Format(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is null)
        {
            return "—";
        }

        var local = value.Value.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var day = local.Date;

        if (day == today)
        {
            return $"今天 {local:HH:mm}";
        }

        if (day == today.AddDays(-1))
        {
            return $"昨天 {local:HH:mm}";
        }

        var days = (today - day).Days;
        return days > 1 ? $"{days} 天前" : $"{local:yyyy-MM-dd}";
    }

    public static string Format(DateTimeOffset? value) => Format(value, DateTimeOffset.Now);
}

using ClearC.Core.Formatting;

namespace ClearC.Core.Tests;

public sealed class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(2147483648, "2.00 GB")]
    public void Format_UsesBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }
}

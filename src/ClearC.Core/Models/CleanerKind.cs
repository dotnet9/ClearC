namespace ClearC.Core.Models;

/// <summary>
/// 决定用哪个清理器处理目标。<see cref="None"/> 表示仅分析，不提供删除入口。
/// </summary>
public enum CleanerKind
{
    None,
    DirectoryContents,
    FilePattern,
    Command,
    RecycleBin,
    CodexConversations
}

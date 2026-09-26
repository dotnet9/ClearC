namespace ClearC.Desktop.ViewModels;

/// <summary>
/// 关闭保护：扫描/清理进行中时关闭窗口必须二次确认，避免出现"半清理"状态。
/// 逻辑独立成纯函数，便于单元测试。
/// </summary>
public static class CloseGuard
{
    /// <summary>是否需要弹出"取消 / 停止并退出"确认。</summary>
    public static bool RequiresConfirmation(WorkflowState state) =>
        state is WorkflowState.Scanning or WorkflowState.Cleaning;

    /// <summary>"停止并退出"：先取消，再等待当前项安全结束。</summary>
    public static TimeSpan GracefulStopTimeout => TimeSpan.FromSeconds(15);
}

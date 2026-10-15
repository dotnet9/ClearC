using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;
using ClearC.Desktop.I18n;
using CodeWF.Toolkit.Core.Extensions;
using CodeWF.Toolkit.Core.UpdateChecking;
using ReactiveUI;

namespace ClearC.Desktop.ViewModels;

/// <summary>关于窗口复用应用更新状态，版本来自程序集发布元数据。</summary>
public sealed class AboutViewModel
{
    public AboutViewModel(UpdateViewModel update)
    {
        Update = update;
        var assembly = typeof(App).Assembly;
        VersionText = UpdateVersion.Display(assembly);
        PlatformText = $"{RuntimeInformation.FrameworkDescription} · Avalonia {typeof(Avalonia.Application).Assembly.GetName().Version}";
        CompileTimeText = assembly.CompileTime()?.ToString("yyyy-MM-dd HH:mm:ss", AppLocalizer.Instance.Culture) ?? "—";
        CopyrightText = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
        CloseCommand = ReactiveCommand.Create(() => RequestClose?.Invoke(this, EventArgs.Empty));
    }

    public UpdateViewModel Update { get; }
    public string VersionText { get; }
    public string PlatformText { get; }
    public string CompileTimeText { get; }
    public string CopyrightText { get; }
    public ICommand CloseCommand { get; }
    public event EventHandler? RequestClose;
}

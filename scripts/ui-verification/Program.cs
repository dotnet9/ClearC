using Avalonia;
using Avalonia.Headless;
using ClearC.Desktop;

// 使用真实 Skia 渲染，不创建系统窗口，也不占用鼠标键盘。
var output = args.Length > 0 ? args[0] : "artifacts/ui-managed";
var builder = AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia();
return SelfCheck.Run(output, builder);

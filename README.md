# ClearC

ClearC 是一个面向 Windows 的 C 盘空间分析与安全清理工具，使用 .NET 10、Avalonia 12、Semi.Avalonia 和 ReactiveUI 构建。

项目的重点不是“尽可能多删”，而是把每个清理目标的容量、风险、执行方式和失败原因展示清楚。低风险缓存默认选中；会造成不可恢复结果或需要重新下载依赖的项目必须手动选择并再次确认。

## 功能

- 实时分析系统盘容量及常见开发缓存占用。
- 扫描 NuGet、npm、用户临时目录、Edge/Chrome 缓存和回收站。
- Codex 会话记录默认不选；用户主动勾选且 Codex 已关闭时，仅清理 `sessions` 与 `archived_sessions`。
- 只展示 `Windows.old`、休眠文件、页面文件和内存转储，不提供删除入口。
- NuGet 与 npm 优先调用各自官方清理命令。
- 清理 NuGet 全局包前检查已加载的缓存 DLL，发现 IDE/MSBuild 占用时整项跳过。
- 临时文件仅清理超过 7 天未修改的文件；占用或无权限文件会跳过并记录。
- 自绘标题栏、磁盘环图、分类筛选、风险确认、实时日志和六态工作流。

## 安全边界

| 目标 | 默认选择 | 执行方式 | 风险处理 |
| --- | --- | --- | --- |
| NuGet HTTP / 临时 / 插件缓存 | 是 | `dotnet nuget locals ... --clear` | 官方命令重建 |
| NuGet 全局包缓存 | 否 | 官方命令 | 检测 DLL 占用；需单独确认 |
| 用户临时文件 | 是 | 白名单目录内逐文件清理 | 仅清理 7 天前文件，保留目录本身 |
| npm 缓存 | 是 | `npm cache clean --force` | 命令失败时保留错误信息 |
| Edge / Chrome 缓存 | 是 | 仅清理明确的 Cache / Code Cache / GPUCache 等目录 | 不触碰历史、登录数据和收藏夹 |
| 回收站 | 否 | Windows Shell API | 清空后不可恢复，需单独确认 |
| Codex 会话记录 | 否 | ClearC 本地清理固定的 `sessions` / `archived_sessions` | 高风险二次确认；Codex 运行时整项跳过 |
| `.codex` 其他数据、用户文档、下载、桌面、源码仓库 | 否 | 不提供清理器 | 永不由 ClearC 删除 |
| Windows.old、hiberfil.sys、pagefile.sys、MEMORY.DMP | 否 | 仅分析 | 交给 Windows 设置或系统工具管理 |

ClearC 不会自动结束 IDE、`dotnet`、MSBuild 或浏览器进程。遇到占用时应关闭相关程序后重新扫描。

## 环境

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（从源码构建时需要）

仓库通过 `global.json` 固定 SDK 功能带，并允许使用同一功能带的最新补丁。

## 构建和运行

```powershell
dotnet restore ClearC.slnx
dotnet build ClearC.slnx
dotnet run --project src/ClearC.Desktop/ClearC.Desktop.csproj
```

## 测试

```powershell
dotnet test tests/ClearC.Core.Tests/ClearC.Core.Tests.csproj
dotnet test tests/ClearC.Desktop.Tests/ClearC.Desktop.Tests.csproj
```

测试覆盖容量格式化、选择与风险策略、目录扫描、受保护清理、NuGet 锁检测、六态工作流和 Avalonia 无窗口渲染。

## 发布

执行：

```bat
publish_win-x64.bat --no-pause
```

自包含程序输出到：

```text
artifacts\publish\win-x64\ClearC\ClearC.Desktop.exe
```

目标机器不需要预装 .NET Runtime。发布脚本默认生成 NativeAOT、裁剪后的 Windows x64 程序。

## 项目结构

```text
src/ClearC.Core/          清理模型、选择规则、安全策略与接口
src/ClearC.Desktop/       Avalonia UI、Windows 扫描和清理实现
tests/                    核心测试、Windows 适配器测试和无窗口 UI 测试
design/                   六个状态的 HTML/CSS/JS 视觉原型
scripts/                  可重复执行的发布脚本
```

## 贡献

提交改动前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。安全问题请按 [SECURITY.md](SECURITY.md) 私下报告，不要公开包含敏感路径或个人数据的日志。

## 许可证

[MIT](LICENSE)

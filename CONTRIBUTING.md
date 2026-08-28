# Contributing to ClearC

感谢你参与 ClearC。磁盘清理软件的改动可能直接影响用户数据，因此安全性和可审查性优先于功能数量。

## 开发流程

1. 从最新的 `main` 创建功能分支。
2. 先为行为边界添加或更新测试。
3. 保持改动小而集中，不在同一提交中混入无关重构。
4. 运行构建和两组测试。
5. 提交 Pull Request，并说明目标、风险、验证方式和未覆盖部分。

```powershell
dotnet restore ClearC.slnx
dotnet build ClearC.slnx --no-restore
dotnet test tests/ClearC.Core.Tests/ClearC.Core.Tests.csproj --no-build
dotnet test tests/ClearC.Desktop.Tests/ClearC.Desktop.Tests.csproj --no-build
```

## 清理器要求

新增清理目标必须同时满足：

- 有稳定、明确、可验证的目标路径或官方清理命令。
- 不使用宽泛通配符定位删除根目录。
- 符号链接和重解析点默认跳过。
- 文件占用、无权限和路径变化不会中断整次任务。
- 影响可恢复性或需要重新下载的目标不是默认选择。
- 用户文档、下载、桌面、源码仓库和整个 `.codex` 目录不得加入通用文件清理白名单。
- Codex 会话清理只能使用专用清理器，固定处理 `.codex\sessions` 与 `.codex\archived_sessions`；不得调用或连接 Codex，检测到 Codex 进程时必须整项跳过。
- 不自动终止进程；如确有必要，必须设计独立的显式确认流程。

每个新增目标都应有扫描测试、清理白名单测试、拒绝路径测试和失败结果测试。

## 代码风格

- 使用仓库的 .NET SDK、中央包版本和可空引用设置。
- 延续现有命名空间、目录和 MVVM 边界。
- 不为了少量重复引入新依赖或抽象层。
- UI 文案使用自然、明确的简体中文；代码和提交信息使用英文。
- 提交信息遵循 [Conventional Commits](https://www.conventionalcommits.org/)：`feat:`、`fix:`、`test:`、`docs:`、`chore:`。

## 视觉改动

`design/` 是六态界面的视觉与交互基准。修改窗口结构、颜色、间距或状态流时，应同时提供 `1060×700` 下的前后截图，并运行无窗口 UI 测试。

## 报告问题

普通缺陷可以创建 GitHub Issue。涉及任意删除越界、路径验证绕过或敏感日志泄露的问题，请按 [SECURITY.md](SECURITY.md) 私下报告。

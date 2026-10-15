# 界面与功能验收

本轮版本 0.2.6。保持现有清理产品布局，对照 design 原型校准标题栏、132px 磁盘环图、统计区间距、扫描筛选行和结果列表；结果区不再覆盖筛选工具栏。

应用资源输出到 I18n，强类型键由 T4 生成；库文案随 NuGet 内嵌，宿主正常加载资源即可同键覆盖。关于窗口的版本、平台和编译时间取自运行环境及程序集。更新检查、下载校验和安装统一调用 IUpdateService，宿主只管理状态、进度与取消。

## 验证

```powershell
dotnet test ClearC.slnx -c Release
pwsh scripts/publish.ps1 -RuntimeIdentifiers win-x64
# 在发布目录运行，保存截图路径可自行指定。
ClearC.Desktop.exe --selfcheck --capture E:/github/temp/ClearC.NativeQA
```

Windows x64 NativeAOT 自检退出 0，输出 23 张 PNG：八色各自浅深、双语关于窗口，以及扫描、结果、确认、清理和完成五个状态。工作流用可控扫描器和执行器驱动实际 ViewModel / AXAML，检查状态迁移、选中结果、结果列表与筛选行的布局；所有样例路径及文件均隔离，不触碰用户清理目录。

![结果视图](media/native-results.png)

![关于窗口](media/native-about.png)

## 验收边界

锁屏期间完成离屏渲染和代码回归，没有执行人工鼠标/键盘验收。多 DPI、跨屏、管理员重启、真实 GitHub 安装包及系统安装器尚需人工验收；受控清理自检不代表真实用户缓存的全量清理验证。

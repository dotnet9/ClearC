# 更新日志

## 0.2.3 (2026-10-04)

### 全平台 NativeAOT + 安装包

- 发布矩阵补齐为 5 平台：win-x64、linux-x64、linux-arm64、osx-x64（Intel）、osx-arm64（Apple Silicon），全部 NativeAOT（完整反射元数据保全，单线程 ILC）；目标框架统一下调到 .NET 10（10 正式版工具链在 macOS 上的 AOT 链接稳定，替代 .NET 11 preview 的 swift auto-link 缺陷）。
- 非 Windows 平台改用真正的安装包：Linux 产出 `.deb`（amd64/arm64），macOS 产出 `.dmg`（Intel/Apple Silicon），Windows 维持既有安装器/分发形态。
- 平台相关功能尚未适配时，应用内给出友好提示（如「当前平台功能正在开发中」），不阻塞启动与其余功能。

## 0.2.2 (2026-10-04)

### 更新检查摆脱 API 配额

- 版本检查改走 GitHub 网页端点：`releases/latest` 的 302 落点直接给出最新版本号（只读响应头），版本变化时再经 `expanded_assets/{tag}`（发布页懒加载资产列表的接口）解析安装包直链——全程不碰 api.github.com 的每小时配额，代理共享出口 IP 也不会再看到「GitHub API 限流中」。
- Releases API 降级为自动回退（403/429 按 Retry-After / X-RateLimit-Reset 退避），网页端点改版或超时时才启用；下载与校验流程不变。

## 0.2.1 (2026-10-01)

# ClearC v0.2.1

### 新增

- **自动更新**：状态栏右下角常驻「检查更新」按钮，启动时也会后台自动检查；发现新版本后可直接下载（带进度条、SHA-256 校验）并一键运行安装器升级。
- 安装包下载使用临时文件 + 原子改名，避免半截安装包被误运行；校验失败自动清理重下。

### 安装包

- `ClearC-v0.2.1-win-x64-setup.exe`（Inno Setup 简体中文向导）
- `ClearC-v0.2.1-win-x86-setup.exe`
- 均附 `.sha256` 校验文件。


**Full Changelog**: https://github.com/dotnet9/ClearC/compare/v0.2.0...v0.2.1

## 0.2.0 (2026-09-30)

# ClearC v0.2.0

### 新增

- **打标签自动发布**：推送 `v*` 或纯数字标签（如 `v0.2.0`）即触发 GitHub Actions——先跑全部测试，再发布 win-x64 / win-x86 的 NativeAOT 自包含程序并执行 `--selfcheck` 冒烟验证，最后自动创建 GitHub Release。
- **Windows 安装包**：使用 Inno Setup 生成简体中文安装向导，本版本提供：
  - `ClearC-v0.2.0-win-x64-setup.exe`
  - `ClearC-v0.2.0-win-x86-setup.exe`
  - 每个安装包均附 `.sha256` 校验文件，下载后可核对完整性。
- **本地打包脚本** `scripts/build_installer.ps1`（需本机安装 Inno Setup 6），方便出包前本地验证安装流程。
- `scripts/publish.ps1` 新增可选 `-Version` 参数，发布时把版本号写入程序集元数据。

### 安装

下载对应架构的 `setup.exe` 双击安装，安装向导为简体中文，可选择是否创建桌面快捷方式；卸载请走「设置 → 应用」或安装目录自带的卸载程序。


**Full Changelog**: https://github.com/dotnet9/ClearC/compare/0.1.0...v0.2.0

## 0.1.0 (2026-08-28)

### 中文

**Release Title**

ClearC v0.1.0 - 首个公开版本

**Release Notes**

ClearC 是一款面向 Windows 的 C 盘空间分析与安全清理工具。

本次发布包括：

- 分析系统盘及常见开发缓存占用
- 清理 NuGet、npm、临时文件和浏览器缓存
- 支持回收站清理及高风险操作二次确认
- 展示 Windows.old、休眠文件等系统占用
- NativeAOT 编译，Windows x64 开箱即用

> 建议清理前关闭 IDE、浏览器及其他可能占用缓存文件的程序。

### English

**Release Title**

ClearC v0.1.0 - Initial Release

**Release Notes**

ClearC is a Windows disk analysis and safe cleanup tool.

This release includes:

- System drive and development cache analysis
- Cleanup for NuGet, npm, temporary files, and browser caches
- Recycle Bin cleanup with confirmation for high-risk actions
- Visibility into Windows.old, hibernation files, and other system usage
- NativeAOT-compiled Windows x64 build with no runtime installation required

> Close IDEs, browsers, and other applications that may lock cache files before cleanup.


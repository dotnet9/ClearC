# 0.2.13 本地候选版本媒体复核

2026-10-16，Windows x64。PNG 从独立 NativeAOT UI 验证器捕获逐字节复制，绘制实际应用控件；全部图片、导出、偏好或工作流输入均在临时目录隔离，未执行安装器。

生产及验证源码在验证时尚未提交，冻结指纹 `dfcccd5f39984437a98599b7d7cf2b36f8b1b461a6c4a317ded25a4628af6dc4`（119 个文件）。指纹规则为排序后 `路径 + NUL + 文件小写 SHA-256 + LF` 的 UTF-8 SHA-256，清单为 `E:/github/temp/recheck25-clearc-source-manifest.json`。旧标签和历史日志不替代当前候选字节的验证。

验证完成后，生产修复提交为 `58e0d2ea6c9882a5e1e989e2c3680a1b0773e741`，验证与原型提交为 `e01b7eec5335ad9f9b3ef3e5d50e2e0ab9723606`。提交后的工作区冻结文件逐字节复核一致；此指纹不是 Git 树哈希。本文收口时未推送、打标签或公开发布。

| 媒体文件 | SHA-256 |
| --- | --- |
| `docs/media/native-main-v0.2.13.png` | `6051954a3853dd2c526723b2326f72200aa833a8180325269128a8cc122f0d1c` |
| `docs/media/native-checkbox-v0.2.13.png` | `c2eb4dd88a296f6e33ea5d5ba47f26e4c5b08e5ad7ffea93e679eae4d7777596` |
| `docs/media/native-about-v0.2.13.png` | `047db4544fb82b46fde7d91ddf9b271552043f52718c8351f403f2f0f7f31edb` |
| `docs/media/native-workflow-v0.2.13.gif` | `1e989f79e733f056cefa5403ff26a188b834d2552296802419e4ba2983a43c96` |

正式产品由仓库 `scripts/publish.ps1` 构建；实际 EXE 的隔离 `--verify-runtime` 已通过，版本 `0.2.13.0`。独立验证器实际执行：既有完整 SelfCheck 通过，新增复选框状态断言 96/96 通过，输出 `NativeAOT=True`。覆盖实际 CheckBox 选中勾与底色在十六主题下的六种状态，以及既有 SelfCheck 的隔离目录、完整工作流、外观、语言和更新行为。本轮状态取色 96 条，启用选中图标最低对比度 4.8498；禁用态单独检查弱化反馈。复选框单图来自实际控件的独立状态夹具，主窗和工作流图来自实际应用视图。

托管相同状态断言通过；测试 196/196、离线原型 32 套组合和定位检查通过。原型/原生公共区域的坐标对照见 `E:/github/temp/recheck25-clearc-final-layout.log`，仅证明列出区域的 2 DIP 容差，不等于整页 95% 像素还原。

原 `.iss` 样式保留，基于逐文件核验且剔除 PDB/DBG 的临时 staging 编译 Windows 安装器，未执行。精确产品、验证器、安装包、包与日志哈希见 `E:/github/temp/recheck25-clearc-final-receipt.json`。第三方裁剪/AOT 告警原样保留。尚未人工验证安装/升级/卸载、系统文件选择器、IME、多 DPI、跨屏或其他系统桌面操作。ClearC 的正常产品入口及实际清理仍仅支持 Windows；其他 RID 构建不代表跨平台清理可用。

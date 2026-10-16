# 0.2.13 按钮修复后的本地原生媒体复核

2026-10-16，Windows x64。本文对应复选框修复之后新增的主按钮、危险按钮和内嵌文字修复。旧 `recheck25-native-media.md`、旧媒体与旧临时收据保留其复选框范围；README 改为使用本次重新构建的真实 NativeAOT 输出。本机验证记录不替代 GitHub Release 发布状态。

生产、测试、原型及构建脚本共 163 个输入冻结为 `139190d301956afd267abdfdf3018c89e1cef39a43bf1774e8328e8719ec02f8`。规则为排序后 `路径 + NUL + 文件小写 SHA-256 + LF` 的 UTF-8 SHA-256；这是工作区文件字节指纹，不是 Git 树哈希。完整清单 `E:/github/temp/recheck25-clearc-buttons-source-manifest.json`，产品、UI、包、日志、安装包和媒体哈希见 `E:/github/temp/recheck25-clearc-buttons-final-receipt.json`。

| 媒体文件 | SHA-256 |
| --- | --- |
| `docs/media/native-main-v0.2.13-buttons.png` | `1eaddae26d080ca20e420fb32f0b99f08082f237e9718a0d9581bba412e0369a` |
| `docs/media/native-main-dark-v0.2.13-buttons.png` | `8103092747924f6dce7095a42464ab21accc128cee6f1f1b89db08048c9f90e7` |
| `docs/media/native-checkbox-v0.2.13-buttons.png` | `c2eb4dd88a296f6e33ea5d5ba47f26e4c5b08e5ad7ffea93e679eae4d7777596` |
| `docs/media/native-primary-v0.2.13-buttons.png` | `f25df9b6fe9781439756d9325ca4dbac8aa2bdfb3ad062d011d0902a8d160dbc` |
| `docs/media/native-danger-v0.2.13-buttons.png` | `0f7cab230325070be21f580fe5f0d942c6814349e80a07bb3c4b4f78f901ca01` |
| `docs/media/native-about-v0.2.13-buttons.png` | `79441a5459ee7fb27eb912f39947873b491be60e17b56b568e413f608aa95dc9` |
| `docs/media/native-workflow-v0.2.13-buttons.gif` | `57bcbcaab8445c95e057e80d9ede902ead3476c37f41a47ad2da5a7c8ec9f9b3` |

PNG 逐字节复制自独立 NativeAOT 验证器。主窗、工作流和关于页绘制真实应用视图；复选框、主按钮和危险按钮单图绘制从真实视图提取的控件及实际模板。GIF 为六个受控状态组成的 12.2 秒序列，不是人工桌面操作录像。全部工作流、导出和偏好使用临时隔离输入，没有执行真实磁盘清理。

正式产品由原 `scripts/publish.ps1` 发布，实际 EXE 的隔离 `--verify-runtime` 通过，版本 `0.2.13.0`。独立验证器完整 SelfCheck、复选框 **96/96**、四处实际按钮 **320/320** 均通过，日志明确 `NativeAOT=True`。按钮来自扫描操作、关于更新、确认清理和停止退出；覆盖八配色 × 浅深两态 × normal/hover/pressed/disabled/reenabled，读取实际文字、图标及 `PART_Root` 背景，并模拟指针状态。夹具命令禁止执行，只用于恢复真实按钮可用性。

启用按钮文字最低对比度 **4.8498:1**，危险按钮最低 **5.1025:1**；选中复选框最低 **4.8498:1**。禁用态独立检查不可用性与透明度弱化，继承的 Semi 禁用文字色不套用启用态阈值。初始临时探针中的部分主按钮记录实际禁用，已废止该启用态统计。后以旧冻结正式构建保留的原始编译程序集、有效夹具命令和真实文字重做128项基线，原primary启用normal有14/16低于4.5（最低1.7365），danger为16/16失败（3.7631）；来源与程序集哈希见 `E:/github/temp/recheck25-clearc-buttons-baseline-enabled/baseline-provenance.json`。修复后以本次320项严格断言为准。

托管相同完整链通过；Core/Desktop **196/196**，离线原型 **32** 组语言/外观、**96** 项复选框、四处实际按钮 **320** 项状态及六个工作流通过，定位键与单文件原型新鲜度通过。原型/原生的 **1008** 项坐标对照覆盖 **42** 个视图，容差 **2 DIP**，只证明列出区域的布局，不等于整页 95% 像素还原。

原 `.iss` 视觉设计保留。安装包从逐文件 SHA-256 核验且无 PDB/DBG 的临时 staging 编译，未执行。第三方裁剪/AOT 告警原样保留；未人工验证安装/升级/卸载、系统文件选择器、IME、多 DPI、跨屏或其他系统桌面操作。ClearC 正常入口和真实清理仍仅支持 Windows，其他 RID 构建不证明其他系统清理可用。

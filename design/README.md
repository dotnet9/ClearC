# ClearC 单文件交互原型

唯一入口为 `design/index.html`。将它单独复制到任意空目录即可离线操作，不依赖相邻文件、本地服务、网络图片或字体。六个工作流视图共用一个窗口：待机、扫描、结果、确认、清理、完成。所有路径与清理结果均为受控示例，不执行真实文件操作。

## 外观与交互

窗口基线为 1060 × 700，标题栏包含 Ursa 风格三态按钮、八套 Studio 配色圆点、展开真实选项的语言图标及窗口操作，不重复放置下拉框。主题、配色和语言即时更新并持久化，系统模式监听系统明暗变化，下拉文本左对齐，支持减少动效偏好。

0.2.13 起，盘符与结果列表的选中复选框采用 Primary / OnPrimary，避免深色模式的白勾落在明亮 Accent 上。十六套配色均检查普通、悬停、选中、禁用与恢复；启用选中图标至少达到 3:1 对比度。

清理流程保留多盘选择、扫描动画、结果勾选、风险策略、分组折叠、行详情、定位与复制路径右键菜单、取消、风险确认、日志复制／导出、关于和更新检查六种状态。窗口拖动、最大化、最小化、关闭保护及重启均可在浏览器内演示。默认仅勾选 C: 上可清理的低风险任务，其余需要用户选择。

## 源码与生成

| 位置（相对仓库根目录） | 用途 |
| --- | --- |
| `scripts/prototype/index.template.html` | 唯一窗口结构 |
| `scripts/prototype/assets/theme.css` | 布局、字号、间距和动效 |
| `scripts/prototype/assets/app.js` | 真实可交互的原型状态机 |
| `scripts/prototype/assets/data.js` | 盘符与 21 个任务样例 |
| `scripts/prototype/assets/palette-tokens.js` | Studio 八配色资源 |
| `scripts/prototype/zh-CN.json`、`en-US.json` | 原型专用语义资源键 |
| `scripts/prototype/build-resources.cjs` | 合并应用 JSON 与原型资源 |
| `design/tests/` | 离线交互与真实布局对照脚本 |

维护源码后执行：

```powershell
pwsh scripts/build_standalone_prototype.ps1
pwsh scripts/build_standalone_prototype.ps1 -Check
npm ci --prefix design/tests
# 首次在 design/tests 目录执行 npx playwright install chromium。
npm test --prefix design/tests
```

生成过程需要 Node.js；查看交付 HTML 不需要。不要直接修改生成的 HTML。CI 将其复制到空目录，阻止外部文件和网络请求，遍历 32 个语言／配色／明暗组合并检查完整工作流，拒绝脚本错误及英文界面残留中文。

## 与原生实现的对照范围

原型与 Avalonia 实现共享布局基线、主题资源和语义文案；42 个视图的窗口、工作区、环图、结果列表、日志及状态栏共 1008 个坐标／尺寸值由脚本对照，容差 2 DIP。具体修正、命令和边界见 [界面验收记录](../docs/UI-VERIFICATION.md)。

坐标吻合不等于整页像素一致：原生字体度量、Semi 输入控件、窗口按钮和动画细节仍有差异。原型有 21 个演示任务，原生验收使用 3 个隔离任务以验证真实 ViewModel 流程，不要求样例行数一致，也不以删除原型功能来掩盖实现差异。

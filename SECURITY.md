# Security Policy

## Supported versions

ClearC 目前处于 `0.x` 阶段，只为最新提交和最新发布版本提供安全修复。

## Reporting a vulnerability

请通过 GitHub 仓库的 **Security → Report a vulnerability** 私下提交安全报告。不要为以下问题创建公开 Issue：

- 删除路径逃逸、白名单绕过或符号链接跟随；
- 在未确认时清理中高风险项目；
- 日志、异常或截图泄露个人路径和文件内容；
- 自动结束无关进程或破坏开发环境；
- 发布包被替换、篡改或加载非预期代码。

报告中请包含受影响版本、复现步骤、预期与实际结果，以及不包含个人数据的最小日志。维护者确认问题前，请不要在公开渠道披露利用细节。

## Scope

安全策略覆盖本仓库中的 ClearC 代码和官方发布产物。第三方 CLI（如 `dotnet`、NuGet、npm）自身的漏洞应同时报告给对应上游项目。

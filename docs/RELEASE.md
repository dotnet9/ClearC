# ClearC 发布流程

版本号取自 `Directory.Build.props`；本轮版本为 0.2.10。主分支提交与附注标签 `v0.2.10` 保持一致，发布说明维护在 `.github/release-notes/v0.2.10.md`，标签附注复用该文件。

## 发布前验证

执行 [界面验收记录](UI-VERIFICATION.md) 中的单元测试、T4、单文件原型生成校验、浏览器离线审计、真实应用离屏验证和布局对照。Windows 正式发布必须成功，独立原生验证器也必须编译并实际运行成功：

```powershell
pwsh scripts/publish.ps1 -RuntimeIdentifiers win-x64
$process = Start-Process ./artifacts/publish/win-x64/ClearC/ClearC.Desktop.exe -ArgumentList '--verify-runtime', 'artifacts/runtime-verification/win-x64' -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'NativeAOT runtime verification failed.' }
dotnet publish scripts/ui-verification/UiVerification.csproj -c Release -r win-x64 --self-contained true -m:1 -p:PublishAot=true -p:PublishTrimmed=true -p:DebugType=portable -p:DebugSymbols=true -p:IlcGenerateCompleteTypeMetadata=true -p:IlcTrimMetadata=false -p:IlcSingleThreaded=true -o artifacts/ui-native/win-x64
artifacts/ui-native/win-x64/UiVerification.exe artifacts/ui-evidence/win-x64
node design/tests/compare-layout.cjs artifacts/ui-evidence/win-x64 artifacts/prototype-audit
```

本地验证 NuGet 可使用 `temp_nugets` 环境变量指向的源；提交的包引用使用公开版本，不提交本地源配置。

`--verify-runtime <独立目录>` 直接执行正式原生 EXE，检查发布目录的两份翻译及语言切换，并在指定目录使用实际配置服务完成外观偏好写入和回读。不初始化桌面、单实例、日志或系统清理，也不读写用户配置。五平台 CI 均执行此项；这只验证分发与配置能力，不代表非 Windows 平台已支持清理。

## 提交、标签与 CI

按修改范围用中文简体规范提交并推送 main，然后创建附注标签：

```powershell
git tag -a v0.2.10 -F .github/release-notes/v0.2.10.md
git push origin v0.2.10
```

`release.yml` 先测试并检查原型与真实界面，再分别在五个原生 runner 编译正式程序和 NativeAOT 界面验证器。界面证据单独上传，不能混入 Release 安装包目录。准备发布说明时优先读取精选说明文件，再读取附注标签；两者均缺失会失败，不能生成只有版本标题的发布页。

## 安装包矩阵

| RID | 安装包 | 原生 runner |
| --- | --- | --- |
| win-x64 | Inno Setup `.exe` | Windows x64 |
| linux-x64 | `.deb`（amd64） | Linux x64 |
| linux-arm64 | `.deb`（arm64） | Linux arm64 |
| osx-x64 | `.dmg` | macOS Intel |
| osx-arm64 | `.dmg` | macOS Apple Silicon |

只发布五个安装包及各自的 `.sha256`；不发布便携 ZIP、PDB、DBG 或 dSYM。`verify_release_installers.ps1` 会检查版本、平台、非空、唯一性、真实散列和多余文件，缺任一项即阻止发布。

Linux deb 提供命令行启动器、桌面菜单及图标，安装容量按剔除符号后的分发文件计算。本机通过隔离 staging 夹具检查目录结构、参数、翻译、图标与符号剔除；真实 dpkg 构建由 Linux runner 执行。

macOS 使用标准 `.app/Contents/MacOS` 与 `Contents/Resources/I18n` 布局，打包后必须成功签名并执行严格签名校验。默认使用 ad-hoc 签名，不等于 Apple 公证；配置 Developer ID 时使用对应身份，不允许签名失败降级继续发布。

## 完成判据与限制

标签触发不等于发布完成。须确认五个平台任务通过、Release 说明可读、10 个资产齐全且散列正确。Windows 本机已验证正式 AOT 及原生离屏行为；跨平台安装器的实际安装和桌面交互须在各自系统验证。

ClearC 的实际清理能力仍仅支持 Windows。Linux/macOS 正式程序会提示并退出（码 2），相应安装包目前只验证分发和构建能力，不应描述为已支持这些平台的清理功能。

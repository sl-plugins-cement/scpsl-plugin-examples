# 可以看看这些（ai写的参考就行 有问题 @我）

本批课程目前在 `onboarding-foundations-zh` 审阅分支；下方命令明确克隆该分支。

先学习提示显示、玩家按键和自定义物品。
不需要下载维护者的整个开发目录，也不需要生产服务器权限。

| 仓库 | 负责什么 | 从哪里读起 |
| --- | --- | --- |
| [scpsl-plugin-examples](https://github.com/sl-plugins-cement/scpsl-plugin-examples) | 中文教程、样式预览、可构建的完整示例 | 本页 |
| [serverkeybinds](https://github.com/sl-plugins-cement/serverkeybinds) | 统一管理服务器专属设置与按键 | `docs/入门.md` |
| [customitems](https://github.com/sl-plugins-cement/customitems) | 物品序列号身份与持有模型工具 | `docs/入门.md` |
| [HintServiceMeow](https://github.com/sl-plugins-cement/HintServiceMeow) | Cement 维护的 HSM 源码分支 | `README.md` |

库接口说明放在对应仓库。完整教学插件放在示例仓库。
这些库是共享依赖 DLL，不是独立玩法插件。HSM 是另一个项目；本仓库讲它的用法及 Cement 的界面约定。

## 第一次构建

1. 安装 Git、支持 C# 12 的 .NET SDK（例如 .NET 8 SDK）和 SCP:SL 专用服务器。
2. 确认 GitHub 账号能够读取表中的四个私有仓库。没有权限时向维护者申请；不要互相发送访问令牌。
3. 克隆本仓库，在 PowerShell 中运行：

```powershell
git clone --branch onboarding-foundations-zh https://github.com/sl-plugins-cement/scpsl-plugin-examples.git
cd scpsl-plugin-examples
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-foundations.ps1
```

服务器安装位置不同，使用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-foundations.ps1 -ManagedPath 'D:\Servers\SCPSL\SCPSL_Data\Managed'
```

脚本按 `dependencies.json` 中的完整提交号获取依赖，构建 ServerKeybinds、CustomItems、Cement HSM 和教学插件。
依赖源码位于 `.workspace`，依赖 DLL 位于 `.dependencies`；它们不提交到 Git。
项目通过 NuGet 还原 .NET Framework 4.8 引用程序集，不需要从别人电脑复制开发包。
脚本不会切换或清理已有依赖目录；版本不匹配时，指定新的 `-SourceRoot`。
构建不部署、不启动服务器。

同时修改依赖源码时，可以显式使用自己的同级工作树：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-foundations.ps1 -SourceRoot 'D:\Cement\worktrees' -UseLocalSources
```

该目录应包含 `ServerKeybinds`、`CustomItems` 和 `HintServiceMeow`。此选项跳过提交锁定检查，只用于开发；提交 PR 时说明实际使用的版本。
要构建仓库内全部旧示例，还需要 ToyTricksDemo 使用的 `0Harmony.dll`：

```powershell
dotnet build ScpslPluginExamples.sln -c Release -p:LabApiGlobalDependenciesPath="$PWD\.dependencies"
```

## 学习顺序

1. 阅读 [HSM 样式指南](hsm-style-guide.md)，打开 [浏览器预览](../preview/index.html)（下载后双击）。
2. 按 [基础示例说明](../examples/Foundations/README.md) 安装到个人测试服，执行 `cement gallery`。
3. 阅读 `Plugin.cs` 的 `ClaimBlock` 部分，绑定按键并切换 HUD。
4. 执行 `cement give`，手持教学硬币，测试使用、冷却、丢弃和拾取。
5. 根据 [验收清单](../tests/foundations-verification.md) 记录结果，完成一个小 PR。

第一份 PR 建议只改一件事，例如添加一种短通知样式。先写预期效果，附预览图，再记录游戏内检查。
协作规则见 [CONTRIBUTING.md](../CONTRIBUTING.md)。

## 版本与证据

依赖库以 `dependencies.json` 的提交号为准，不能只看 DLL 的 API 主版本号。
HSM 从 [Cement 源码仓库](https://github.com/sl-plugins-cement/HintServiceMeow) 的锁定提交构建。
版本为 `5.5.1-cement.1`。构建输出同时包含匹配的 `0Harmony.dll`，不下载官方 HSM DLL。
原有 `-DownloadHsm` 参数仍可使用，但 HSM 现在始终从源码构建。

这个版本不等于本机测试服原有 DLL。源码与原有安装的区别见 [来源记录](https://github.com/sl-plugins-cement/HintServiceMeow/blob/main/PROVENANCE.md)。
本次没有部署或验证当前生产进程。真实客户端测试仍按验收清单执行。

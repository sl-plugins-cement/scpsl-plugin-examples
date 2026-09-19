# SCP:SL 插件示例

[English](README.en.md)

**新协作者：[从这里开始](docs/start-here.md) → [HSM 样式指南](docs/hsm-style-guide.md) → [基础联动练习](examples/Foundations/README.md)。**

本批课程覆盖 ServerKeybinds、CustomItems 与 HSM；请先运行 scripts/build-foundations.ps1 获取锁定依赖，再构建整个解决方案。协作要求见 [CONTRIBUTING.md](CONTRIBUTING.md)。

HSM 使用 [Cement 维护的源码分支](https://github.com/sl-plugins-cement/HintServiceMeow)，由构建脚本按提交号获取。

这是为 GitHub `sl-plugins-cement` 组织中的 Cement 团队准备的 LabAPI 教学仓库。每个示例都可以
直接阅读源码并独立构建，只演示一个重点，不会用额外框架隐藏关键代码。

## 包含的项目

| 项目 | 用途 | 输出文件 |
| --- | --- | --- |
| [`src/ToyTricksDemo`](src/ToyTricksDemo/README.md) | 本地 ToyTricksDemo 参考展厅的完整副本，涵盖 AdminToy、命令、事件、清理、配置及严格限制范围的 Harmony 修复 | `ToyTricksDemo.dll` |
| [`examples/CrossPluginRoles/RoleProvider`](examples/CrossPluginRoles/README.md) | 管理一个示例叠加角色，并公开一个很小的查询接口 | `Example.CharacterRoleProvider.dll` |
| [`examples/CrossPluginRoles/RoleConsumer`](examples/CrossPluginRoles/README.md) | 从另一个插件检测该角色，并写入服务器日志 | `Example.CharacterRoleConsumer.dll` |

ToyTricksDemo 副本来自本地提交 `792696a9c5392b1961f977d0d645b1fb46e122ce`
（版本 1.4.3）。其源码与项目 README 均作为完整参考示例保留。

## 构建

环境要求：

- 安装 .NET SDK；项目会自动还原 .NET Framework 4.8 引用程序集
- SCP:SL 专用服务器位于默认 Steam 路径，或通过 `SCP_SL_MANAGED` MSBuild 属性指向
  `SCPSL_Data/Managed` 目录
- ToyTricksDemo 需要 LabAPI 全局依赖目录中的 `0Harmony.dll`

构建全部项目：

```powershell
dotnet build ScpslPluginExamples.sln -c Release
```

如果 Harmony 位于某个端口的插件目录，而不是 LabAPI 全局依赖目录，请把复制的 TTD 项目
指向包含 `0Harmony.dll` 的目录：

```powershell
dotnet build ScpslPluginExamples.sln -c Release -p:LabApiGlobalDependenciesPath="<包含-0Harmony.dll-的目录>"
```

安装方法、命令、配置和冲突请查看各项目链接的 README。最小示例请从
[跨插件角色教程](examples/CrossPluginRoles/README.md)开始。

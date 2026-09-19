# SCP:SL plugin examples

[简体中文](README.md)

新增中文基础课程：[环境搭建](docs/start-here.md)、[HSM 样式](docs/hsm-style-guide.md)、[联动示例](examples/Foundations/README.md)。构建整个解决方案前，请运行 scripts/build-foundations.ps1 准备锁定依赖。

An educational LabAPI repository for the Cement team in the `sl-plugins-cement` GitHub organization.
Every example is source-readable, independently buildable, and intended to show one technique without
hiding the important line behind a framework.

## Included projects

| Project | Purpose | Output |
| --- | --- | --- |
| [`src/ToyTricksDemo`](src/ToyTricksDemo/README.md) | Complete copy of the local ToyTricksDemo reference gallery: AdminToys, commands, events, cleanup, configuration, and carefully scoped Harmony repairs | `ToyTricksDemo.dll` |
| [`examples/CrossPluginRoles/RoleProvider`](examples/CrossPluginRoles/README.en.md) | Owns one example character-role overlay and exposes a tiny public query | `Example.CharacterRoleProvider.dll` |
| [`examples/CrossPluginRoles/RoleConsumer`](examples/CrossPluginRoles/README.en.md) | Detects that role from another plugin and logs it | `Example.CharacterRoleConsumer.dll` |

The ToyTricksDemo copy comes from local commit `792696a9c5392b1961f977d0d645b1fb46e122ce`
(version 1.4.3). Its source and project README are preserved as the full-sized reference example.

## Build

Requirements:

- .NET SDK with .NET Framework 4.8 reference assemblies (the projects restore them automatically)
- A local SCP:SL Dedicated Server install at the default Steam path, or an `SCP_SL_MANAGED` MSBuild
  property pointing to its `SCPSL_Data/Managed` directory
- LabAPI's global `0Harmony.dll` dependency for ToyTricksDemo

Build everything:

```powershell
dotnet build ScpslPluginExamples.sln -c Release
```

If Harmony is installed in a port-specific plugin directory instead of LabAPI's global dependency
directory, point the copied TTD project at the directory containing `0Harmony.dll`:

```powershell
dotnet build ScpslPluginExamples.sln -c Release -p:LabApiGlobalDependenciesPath="<directory-containing-0Harmony.dll>"
```

See each linked project README for installation, commands, configuration, and conflicts. Start with
the [cross-plugin role walkthrough](examples/CrossPluginRoles/README.en.md) for the minimal example.

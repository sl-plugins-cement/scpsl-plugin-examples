# 跨插件角色检测

[English](README.en.md)

这是一个最小但完整的 LabAPI 示例：一个插件检测由另一个插件管理的角色。

- `RoleProvider` 管理名为 `example.instructor` 的叠加角色。为了无需命令和配置即可测试，
  所有设施警卫都会获得该示例角色。
- `RoleConsumer` 监听 `PlayerEvents.ChangedRole`，然后通过提供者的公开 API 查询刚切换角色的玩家。
- 两个插件都不会改变玩法。提供者只保存角色元数据，消费者只写入服务器日志。

跨插件调用只有一行：

```csharp
if (CharacterRoleApi.TryGetRole(player, out string roleId))
{
    // 这个角色由提供者插件管理。
}
```

## 为什么使用这种结构

提供者拥有自己的状态，只公开一个很小的查询接口。消费者使用带 `Private="false"` 的普通
项目引用，因此编译器能够检查 API，同时服务器只加载每个插件 DLL 的一个副本。
`RoleProvider` 使用 `LoadPriority.Highest`，`RoleConsumer` 使用 `LoadPriority.Lowest`。

这是“必需依赖”示例，安装时必须同时放置两个 DLL。如果提供者可能不存在，应创建一个小型
契约程序集，或编写隔离良好的可选集成适配器；不要读取另一个插件的私有字段。

## 构建与测试

在仓库根目录运行：

```powershell
dotnet build ScpslPluginExamples.sln -c Release
```

把以下两个文件复制到同一个 LabAPI 插件目录，然后重启本地服务器：

- `RoleProvider/bin/Release/Example.CharacterRoleProvider.dll`
- `RoleConsumer/bin/Release/Example.CharacterRoleConsumer.dll`

在 RA 中把一名玩家切换为设施警卫。角色切换后，服务器日志应显示：

```text
[RoleConsumerExample] Detected PlayerName as 'example.instructor' from the provider plugin.
```

把玩家切换为其他原生角色后，叠加角色会被移除。此示例没有配置、玩家命令、RA 命令、
提示文本或已知插件冲突。

# 基础联动练习

一个教学插件，分三步读：`Hints.cs` 学显示、`Plugin.cs` 的 `ClaimBlock` 学输入、`ItemRegistry` 学物品身份。
整条流程是：管理员发硬币 → 手持 → 接受建议按键或自己绑定 → 使用 → 显示五秒冷却 → 丢弃与转交。
普通硬币不会获得教学能力。能力只记录一次演示脉冲与冷却，不造成伤害、不消耗硬币；原版抛硬币行为保留。

## 构建与安装

先完成 [环境搭建](../../docs/start-here.md)。只在个人 LabAPI 测试服安装。

| 文件 | 目标位置（相对于 `%APPDATA%\SCP Secret Laboratory\LabAPI`） |
| --- | --- |
| `.dependencies/ServerKeybinds.dll` | `dependencies/global/` |
| `.dependencies/CustomItems.dll` | `dependencies/global/` |
| `bin/Release/Example.CementFoundations.dll`（本项目目录下） | `plugins/<测试端口>/` |
| `.dependencies/HintServiceMeow.dll`，由锁定的 HSM 源码构建 | `plugins/<测试端口>/` |
| `.dependencies/0Harmony.dll`，由 HSM 构建输出 | `dependencies/<测试端口>/`，已有全局兼容版本时不要重复安装 |

同一个共享库只安装一份，不在插件目录再放一份。使用本次构建的依赖，旧版 ServerKeybinds 不一定包含教学 ID 区块。
替换 DLL 后重启该测试端口。不要用同时覆盖所有端口的自动部署选项。
请先提交自己的修改，再复制构建产物；复制完成、服务器加载完成、玩家观察到行为是三个不同状态。

游戏内管理员需要原生 `ServerConfigs` 权限，打开 Remote Admin 执行：

```text
cement gallery
cement give
cement clear
```

`gallery` 显示 12 秒画廊；需要存活角色才能看到。`give` 只向执行者发放，背包满时不发放。
`clear` 关闭画廊；如果仍手持教学硬币，其状态 HUD 会继续显示。
服务器专属设置的 Tools 分类下可以看到“入门练习”。接受 V 建议或另行绑定，然后手持硬币使用。
隐藏 HUD 不会禁止使用物品。把硬币交给其他玩家后，冷却跟随序列号，不能通过转交重置。

## 生命周期与边界

`TrackGranted` 记录发放，`Drop`/`Transfer` 记录转移，每 0.25 秒核对原生物品和拾取物集合，移除已经不存在的序列号。
集合核对适合小规模教学，不应原样复制为每个生产插件各自扫描全服物品的方案。
回合重置或卸载会清空登记；仍存在的硬币变为普通硬币，示例不删除玩家背包。
角色变化移除界面，原生掉落流程保留硬币身份；断线移除该玩家的提示与显示偏好。

这里没有自定义模型。确认身份和生命周期正确以后，再阅读 CustomItems 的 `HeldMeshManager` 和 `HeldMeshSpec`。
跨 SCP-914 转换、其他插件替换序列号等行为不在本课支持范围内。
Settings 可见性只帮助界面呈现；回调仍检查存活状态、当前手持的登记序列号和冷却。

## 复制为自己的插件

改程序集名、命名空间、命令、Hint ID/分组，然后向 ServerKeybinds 维护者申请自己的固定区块。
`SssIdBlocks.CementExamples` 仅供此示例使用；不要安装两个使用同一区块的副本。
验证项目见 [清单](../../tests/foundations-verification.md)。

原生 API 依据（LabAPI 源码相对路径）：
`LabApi/Features/Wrappers/Players/Player.cs` 的 `AddItem`、
`LabApi/Events/Arguments/PlayerEvents/PlayerDroppedItemEventArgs.cs`、
`PlayerPickedUpItemEventArgs.cs`；库契约依据为 CustomItems `src/ItemRegistry.cs`。

using System;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace Cement.Foundations;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class FoundationsCommand : ICommand
{
    public string Command => "cement";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "中文入门练习：cement give / gallery / clear。";
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.ServerConfigs, out _)) { response = "需要 ServerConfigs 权限。"; return false; }
        FoundationsPlugin? plugin = FoundationsPlugin.Instance;
        Player? player = Player.Get(sender);
        if (plugin == null || player == null) { response = "请由游戏内管理员执行，并确认示例已启用。"; return false; }
        string action = arguments.Count == 1 ? arguments.At(0).ToLowerInvariant() : "";
        switch (action)
        {
            case "give": response = plugin.Grant(player); return true;
            case "gallery": response = plugin.Gallery(player); return true;
            case "clear": plugin.Clear(player); response = "已关闭画廊；手持硬币的 HUD 仍由物品状态控制。"; return true;
            default: response = "用法：cement give | gallery | clear"; return false;
        }
    }
}

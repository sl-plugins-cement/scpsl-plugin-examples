using System;
using System.Collections.Generic;
using System.Linq;
using CustomItems;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;
using MEC;
using ServerKeybinds;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Cement.Foundations;

public sealed class FoundationsPlugin : Plugin
{
    public override string Name => "Example.CementFoundations";
    public override string Description => "Cement 中文教学：HSM 样式、共享按键、自定义物品。";
    public override string Author => "Cement";
    public override Version Version => new(1, 0, 0);
    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);
    public override LoadPriority Priority => LoadPriority.Lowest;
    internal static FoundationsPlugin? Instance { get; private set; }
    private enum Kind { TrainingCoin }
    private readonly ItemRegistry<Kind> items = new("CementExamples");
    private readonly Dictionary<ushort, float> readyAt = new();
    private readonly Dictionary<Player, float> galleryUntil = new();
    private readonly HashSet<Player> hidden = new();
    private readonly Dictionary<Player, bool> galleryMode = new();
    private KeybindBlock? block;
    private IHintDisplayProvider hints = new NullHints();
    private CoroutineHandle loop;

    public override void Enable()
    {
        hints = HsmHints.Create();
        block = KeybindRegistry.ClaimBlock(SssIdBlocks.CementExamples, Name)
            .InCategory(SettingsCategory.Tools)
            .Header("Cement 入门练习")
            .VisibleTo(p => p.IsAlive)
            .AddTextArea(3, "请手动接受建议按键 V，或自行绑定。手持教学硬币才能触发练习。")
            .Add(1, "使用教学硬币", KeyCode.V, "触发一次演示脉冲，冷却 5 秒。", Use)
            .AddTwoButtons(2, "教学 HUD", "显示", "隐藏", false, "只影响本示例的显示。", (p, isB) =>
            {
                if (isB) { hidden.Add(p); hints.Clear(p); }
                else hidden.Remove(p);
            });
        block.Enable();
        PlayerEvents.DroppedItem += Dropped;
        PlayerEvents.PickedUpItem += PickedUp;
        PlayerEvents.Left += Left;
        PlayerEvents.ChangedRole += ChangedRole;
        ServerEvents.WaitingForPlayers += Reset;
        loop = Timing.RunCoroutine(Tick());
        Instance = this;
    }

    public override void Disable()
    {
        Instance = null;
        Timing.KillCoroutines(loop);
        PlayerEvents.DroppedItem -= Dropped;
        PlayerEvents.PickedUpItem -= PickedUp;
        PlayerEvents.Left -= Left;
        PlayerEvents.ChangedRole -= ChangedRole;
        ServerEvents.WaitingForPlayers -= Reset;
        block?.Disable();
        block = null;
        Reset();
        hidden.Clear();
        hints.Dispose();
    }

    internal string Grant(Player player)
    {
        if (!player.IsAlive) return "请先切换为存活角色。";
        if (player.Items.Any(i => items.IsTracked(i.Serial))) return "你已持有教学硬币。";
        Item? item = player.AddItem(ItemType.Coin);
        if (item == null) return "发放失败：请检查背包空间。";
        items.TrackGranted(item.Serial, Kind.TrainingCoin, player.UserId, "cement give");
        Logger.Info($"[Cement] Grant serial={item.Serial}");
        return $"已发放教学硬币（序列号 {item.Serial}）；请手持并在设置中绑定按键。";
    }

    internal string Gallery(Player player)
    {
        if (!hints.Available) return "HSM 不可用；查看启动日志，安装兼容版本后重启。";
        if (hidden.Contains(player)) return "请先在服务器专属设置中将教学 HUD 切换为显示。";
        galleryUntil[player] = Time.realtimeSinceStartup + 12f;
        return "已打开 12 秒样式预览；cement clear 可关闭。";
    }

    internal void Clear(Player player) { galleryUntil.Remove(player); galleryMode.Remove(player); hints.Clear(player); }

    private void Use(Player player)
    {
        // 可见性不是业务权限：回调仍检查存活状态、手持物品及冷却。
        Item? held = player.CurrentItem;
        if (!player.IsAlive || held == null || !items.IsKind(held.Serial, Kind.TrainingCoin)) return;
        float now = Time.realtimeSinceStartup;
        if (readyAt.TryGetValue(held.Serial, out float ready) && ready > now) return;
        readyAt[held.Serial] = now + 5f;
        Logger.Info($"[Cement] Use serial={held.Serial} cooldown=5");
        // 本课只展示状态流转；不造成伤害、不消耗物品。
    }

    private IEnumerator<float> Tick()
    {
        while (true)
        {
            // 物品/拾取物转移结束后再核对存活序列号，覆盖销毁、清背包和死亡掉落。
            var live = new HashSet<ushort>(Item.List.Select(i => i.Serial));
            live.UnionWith(Pickup.List.Select(p => p.Serial));
            foreach (ushort serial in items.Snapshot().Keys.Where(s => !live.Contains(s)).ToArray())
            {
                items.Destroy(serial, "world reconciliation");
                readyAt.Remove(serial);
            }
            foreach (Player player in Player.ReadyList)
            {
                bool gallery = galleryUntil.TryGetValue(player, out float until) && until > Time.realtimeSinceStartup;
                if (!gallery) galleryUntil.Remove(player);
                Item? held = player.CurrentItem;
                bool holding = held != null && items.IsTracked(held.Serial);
                if (!player.IsAlive || hidden.Contains(player) || (!gallery && !holding)) { hints.Clear(player); continue; }
                if (!galleryMode.TryGetValue(player, out bool previous) || previous != gallery) hints.Clear(player);
                galleryMode[player] = gallery;
                if (gallery)
                {
                    hints.Show(player, "title", "<color=#4FCBFF>CEMENT · 入门练习</color>", 650f, 30);
                    hints.Show(player, "status", "<color=#E7ECF3>教学硬币</color>  <color=#5BFF80>已就绪</color>", 700f, 24);
                    hints.Show(player, "detail", "<color=#C0C8D4>手持物品后，使用你绑定的按键</color>", 745f, 22);
                }
                else
                {
                    float seconds = readyAt.TryGetValue(held!.Serial, out float ready) ? Math.Max(0, ready - Time.realtimeSinceStartup) : 0;
                    hints.Show(player, "cooldown", seconds > 0
                        ? $"<color=#FFD24D>教学硬币 · 冷却 {seconds:0.0} 秒</color>"
                        : "<color=#5BFF80>教学硬币 · 已就绪</color>", 1050f, 24);
                }
            }
            yield return Timing.WaitForSeconds(0.25f);
        }
    }

    private void Dropped(PlayerDroppedItemEventArgs ev) { if (items.Drop(ev.Pickup.Serial, ev.Player.UserId, "drop")) Logger.Info($"[Cement] Drop serial={ev.Pickup.Serial}"); }
    private void PickedUp(PlayerPickedUpItemEventArgs ev) { if (items.Transfer(ev.Item.Serial, ev.Player.UserId, "pickup")) Logger.Info($"[Cement] Transfer serial={ev.Item.Serial}"); }
    private void Left(PlayerLeftEventArgs ev) { Clear(ev.Player); hidden.Remove(ev.Player); }
    private void ChangedRole(PlayerChangedRoleEventArgs ev) => Clear(ev.Player);
    private void Reset()
    {
        hints.Dispose();
        galleryUntil.Clear();
        galleryMode.Clear();
        readyAt.Clear();
        items.Clear();
    }
}

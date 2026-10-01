using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace Cement.Foundations;

// 业务代码只依赖这个接口；HSM 缺失时使用空实现，不覆盖其他插件的原生提示。
internal interface IHintDisplayProvider : IDisposable
{
    bool Available { get; }
    void Show(Player player, string id, string text, float y, int size);
    void Clear(Player player);
}

internal sealed class NullHints : IHintDisplayProvider
{
    public bool Available => false;
    public void Show(Player player, string id, string text, float y, int size) { }
    public void Clear(Player player) { }
    public void Dispose() { }
}

// 仅封装本课需要的 Center/Middle 布局。精确匹配签名，避免选中 HSM 的集合重载。
internal sealed class HsmHints : IHintDisplayProvider
{
    private const string Group = "cement.foundations";
    private readonly Type hintType;
    private readonly MethodInfo get, add, remove;
    private readonly Dictionary<(Player Player, string Id), (object Display, object Hint)> active = new();
    private bool failed;
    public bool Available => !failed;

    private HsmHints(Assembly assembly)
    {
        hintType = assembly.GetType("HintServiceMeow.Core.Models.Hints.Hint", true)!;
        Type abstractHint = assembly.GetType("HintServiceMeow.Core.Models.Hints.AbstractHint", true)!;
        Type display = assembly.GetType("HintServiceMeow.Core.Utilities.PlayerDisplay", true)!;
        get = display.GetMethod("Get", new[] { typeof(Player) }) ?? throw new MissingMethodException("PlayerDisplay.Get");
        add = display.GetMethod("AddHint", new[] { abstractHint, typeof(string) }) ?? throw new MissingMethodException("AddHint");
        remove = display.GetMethod("RemoveHint", new[] { abstractHint, typeof(string) }) ?? throw new MissingMethodException("RemoveHint");
        foreach (string name in new[] { "Id", "Text", "FontSize", "XCoordinate", "YCoordinate", "Alignment", "YCoordinateAlign" })
            if (hintType.GetProperty(name)?.CanWrite != true) throw new MissingMemberException(name);
    }

    public static IHintDisplayProvider Create()
    {
        try
        {
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "HintServiceMeow");
            if (assembly != null) return new HsmHints(assembly);
            Logger.Warn("[Cement] 未加载 HSM；按键和物品示例仍可用，界面不显示。安装后重启测试服。");
        }
        catch (Exception ex) { Logger.Warn("[Cement] HSM 接口不兼容：" + ex.GetType().Name); }
        return new NullHints();
    }

    public void Show(Player player, string id, string text, float y, int size)
    {
        if (failed) return;
        try
        {
            if (!active.TryGetValue((player, id), out var entry))
            {
                object hint = Activator.CreateInstance(hintType)!;
                Set(hint, "Id", Group + "." + id);
                Set(hint, "XCoordinate", 0f);
                SetEnum(hint, "Alignment", "Center");
                SetEnum(hint, "YCoordinateAlign", "Middle");
                Set(hint, "YCoordinate", y);
                Set(hint, "FontSize", size);
                Set(hint, "Text", text);
                object display = get.Invoke(null, new object[] { player })!;
                entry = (display, hint);
                active[(player, id)] = entry;
                add.Invoke(display, new[] { hint, Group });
            }
            else
            {
                // 复用同一个 Hint，只修改发生变化的文字；不反复 AddHint/ForceUpdate。
                if ((string?)hintType.GetProperty("Text")!.GetValue(entry.Hint) != text)
                    Set(entry.Hint, "Text", text);
            }
        }
        catch (Exception ex)
        {
            failed = true;
            Logger.Warn("[Cement] HSM 调用失败，已关闭本示例界面：" + ex.GetType().Name);
            Dispose();
        }
    }

    public void Clear(Player player)
    {
        foreach (var key in active.Keys.Where(k => k.Player == player).ToArray())
        {
            var entry = active[key];
            try { remove.Invoke(entry.Display, new[] { entry.Hint, Group }); }
            catch (Exception ex) { Logger.Warn("[Cement] 提示移除失败：" + ex.GetType().Name); }
            active.Remove(key);
        }
    }
    public void Dispose()
    {
        foreach (Player player in active.Keys.Select(k => k.Player).Distinct().ToArray()) Clear(player);
    }
    private void Set(object hint, string name, object value) => hintType.GetProperty(name)!.SetValue(hint, value);
    private void SetEnum(object hint, string name, string value)
    {
        PropertyInfo property = hintType.GetProperty(name)!;
        property.SetValue(hint, Enum.Parse(property.PropertyType, value));
    }
}

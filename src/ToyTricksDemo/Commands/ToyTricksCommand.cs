using System;
using System.Globalization;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace ToyTricksDemo.Commands;

/// <summary>
/// RA: toytricks [all|shear|glow|strikealt|text|waypoint|firefight|patrol|calibrate &lt;factor&gt;|clear]. Spawns the
/// demo gallery on the floor in front of the calling admin (default: every bay), tunes the TextToy meter
/// calibration live, or clears it. In-game sender required for spawning (the gallery is placed
/// relative to the caller).
/// </summary>
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ToyTricksCommand : ICommand
{
    public string Command => "toytricks";

    public string[] Aliases => ["ttd"];

    public string Description =>
        "Spawns the toy-tricks demo gallery (shear / HDR glow / strike array / world text / waypoint carrier / moving firefight / walking patrol) or clears it.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (ToyTricksPlugin.Instance?.Config?.IsEnabled != true)
        {
            response = "ToyTricksDemo is disabled by config.";
            return false;
        }

        if (!sender.CheckPermission(PlayerPermissions.ServerConfigs, out _))
        {
            response = "Permission denied: requires ServerConfigs. / 权限不足：需要 ServerConfigs 权限。";
            return false;
        }

        string selection = arguments.Count > 0 ? arguments.At(0).ToLowerInvariant() : "all";
        if (selection == "clear")
        {
            response = DemoGallery.Clear() ? "Gallery removed." : "No gallery to remove.";
            return true;
        }

        // Live TextToy calibration: adjust the factor, then respawn the text bay and re-check the
        // ruler edge markers against the physical bars. Session-only; persist the final value in
        // config.yml (text_units_to_meters) once dialed in.
        if (selection == "calibrate")
        {
            if (arguments.Count < 2
                || !float.TryParse(arguments.At(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float factor)
                || factor <= 0f)
            {
                response = $"Usage: toytricks calibrate <factor>  (current: {ExhibitTools.TextUnitsToMeters:0.####})";
                return false;
            }

            ExhibitTools.TextUnitsToMeters = factor;
            response = $"TextUnitsToMeters = {factor:0.####} (session only — set text_units_to_meters in " +
                       "config.yml to persist). Respawn the gallery to apply.";
            return true;
        }

        if (selection is not ("all" or "shear" or "glow" or "strikealt" or "text" or "waypoint" or "firefight" or "patrol"))
        {
            response = "Usage: toytricks [all|shear|glow|strikealt|text|waypoint|firefight|patrol|calibrate <factor>|clear]";
            return false;
        }

        Player? viewer = Player.Get(sender);
        if (viewer == null || !viewer.IsAlive)
        {
            response = "Run this in-game as an alive player (the gallery spawns in front of you).";
            return false;
        }

        response = DemoGallery.SpawnFor(viewer, selection, ToyTricksPlugin.Instance.Config.SpawnDistanceMeters);
        return true;
    }
}

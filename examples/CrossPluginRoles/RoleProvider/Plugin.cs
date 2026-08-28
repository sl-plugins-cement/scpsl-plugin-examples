using System;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;
using PlayerRoles;

namespace ScpslPluginExamples.CrossPluginRoles.Provider;

/// <summary>
/// Owns the example custom-role state. For a zero-setup demonstration, every Facility Guard receives
/// the "example.instructor" overlay role; the player's native role is never changed by this plugin.
/// </summary>
public sealed class CharacterRoleProviderPlugin : Plugin
{
    public override string Name => "Example.CharacterRoleProvider";

    public override string Description => "Provides one minimal custom-role query API for teaching cross-plugin integration.";

    public override string Author => "Cement";

    public override Version Version => new(1, 0, 0);

    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);

    // LabAPI enables smaller priority values first. Dependency providers should enable before consumers.
    public override LoadPriority Priority => LoadPriority.Highest;

    public override void Enable()
    {
        PlayerEvents.ChangedRole += OnChangedRole;
        PlayerEvents.Left += OnLeft;

        foreach (Player player in Player.ReadyList)
        {
            RefreshRole(player, player.Role);
        }
    }

    public override void Disable()
    {
        PlayerEvents.ChangedRole -= OnChangedRole;
        PlayerEvents.Left -= OnLeft;
        CharacterRoleApi.Clear();
    }

    private static void OnChangedRole(PlayerChangedRoleEventArgs ev) =>
        RefreshRole(ev.Player, ev.NewRole.RoleTypeId);

    private static void OnLeft(PlayerLeftEventArgs ev) =>
        CharacterRoleApi.RemovePlayer(ev.Player.PlayerId);

    private static void RefreshRole(Player player, RoleTypeId nativeRole)
    {
        string? characterRole = nativeRole == RoleTypeId.FacilityGuard
            ? CharacterRoleApi.InstructorRoleId
            : null;

        CharacterRoleApi.SetRole(player, characterRole);
    }
}


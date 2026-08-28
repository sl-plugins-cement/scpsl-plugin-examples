using System;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;
using ScpslPluginExamples.CrossPluginRoles.Provider;

namespace ScpslPluginExamples.CrossPluginRoles.Consumer;

/// <summary>
/// Detects a character role owned by another plugin. The one important line is the
/// CharacterRoleApi.TryGetRole call in OnChangedRole.
/// </summary>
public sealed class CharacterRoleConsumerPlugin : Plugin
{
    public override string Name => "Example.CharacterRoleConsumer";

    public override string Description => "Detects and logs character roles exposed by Example.CharacterRoleProvider.";

    public override string Author => "Cement";

    public override Version Version => new(1, 0, 0);

    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);

    public override LoadPriority Priority => LoadPriority.Lowest;

    public override void Enable() => PlayerEvents.ChangedRole += OnChangedRole;

    public override void Disable() => PlayerEvents.ChangedRole -= OnChangedRole;

    private static void OnChangedRole(PlayerChangedRoleEventArgs ev)
    {
        if (CharacterRoleApi.TryGetRole(ev.Player, out string roleId))
        {
            Logger.Info($"[RoleConsumerExample] Detected {ev.Player.Nickname} as '{roleId}' from the provider plugin.");
        }
    }
}

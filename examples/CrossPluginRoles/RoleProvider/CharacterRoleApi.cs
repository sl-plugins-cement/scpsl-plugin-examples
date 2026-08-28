using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;

namespace ScpslPluginExamples.CrossPluginRoles.Provider;

/// <summary>
/// The small, public surface that other plugins compile against. The provider owns the data;
/// consumers can only ask which example role a player has.
/// </summary>
public static class CharacterRoleApi
{
    public const string InstructorRoleId = "example.instructor";

    private static readonly Dictionary<int, string> RolesByPlayerId = new();

    public static bool TryGetRole(Player? player, out string roleId)
    {
        roleId = string.Empty;
        if (player == null || player.IsDestroyed)
        {
            return false;
        }

        if (!RolesByPlayerId.TryGetValue(player.PlayerId, out string storedRoleId))
        {
            return false;
        }

        roleId = storedRoleId;
        return true;
    }

    internal static void SetRole(Player player, string? roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId))
        {
            RolesByPlayerId.Remove(player.PlayerId);
            return;
        }

        RolesByPlayerId[player.PlayerId] = roleId!.Trim().ToLowerInvariant();
    }

    internal static void RemovePlayer(int playerId) => RolesByPlayerId.Remove(playerId);

    internal static void Clear() => RolesByPlayerId.Clear();
}

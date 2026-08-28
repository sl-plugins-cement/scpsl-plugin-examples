# Cross-plugin character roles

[简体中文](README.md)

This is the smallest complete example of one LabAPI plugin detecting a character role owned by
another plugin.

- `RoleProvider` owns an overlay role named `example.instructor`. It assigns that role to every
  Facility Guard so the example can be tested without commands or configuration.
- `RoleConsumer` listens for `PlayerEvents.ChangedRole` and asks the provider's public API whether the
  changed player has an overlay role.
- Neither plugin changes gameplay. The provider only records metadata, and the consumer only logs.

The cross-plugin call is intentionally one line:

```csharp
if (CharacterRoleApi.TryGetRole(player, out string roleId))
{
    // This role belongs to the provider plugin.
}
```

## Why this shape

The provider owns its state and exposes a narrow public query. The consumer uses a normal project
reference with `Private="false"`, so the compiler checks the API while the server still loads exactly
one copy of each plugin DLL. `RoleProvider` uses `LoadPriority.Highest`; `RoleConsumer` uses
`LoadPriority.Lowest`.

This is a required dependency example. Install both DLLs. If the provider may be absent, define a
small contracts assembly or add a carefully contained optional-integration adapter; do not reach into
another plugin's private fields.

## Build and test

From the repository root:

```powershell
dotnet build ScpslPluginExamples.sln -c Release
```

Copy both files to the same LabAPI plugin directory and restart the local server:

- `RoleProvider/bin/Release/Example.CharacterRoleProvider.dll`
- `RoleConsumer/bin/Release/Example.CharacterRoleConsumer.dll`

Use RA to change a player to Facility Guard. After the role change, the server log should contain:

```text
[RoleConsumerExample] Detected PlayerName as 'example.instructor' from the provider plugin.
```

Changing the player to another native role removes the overlay. There are no configs, player
commands, RA commands, hints, or known plugin conflicts in this example.

# Manual verification

Date: 2026-08-28  
Local test port: 7777  
Server: visible LocalAdmin fork, online mode enabled

## Build

Command:

```powershell
dotnet build ScpslPluginExamples.sln -c Release -p:LabApiGlobalDependenciesPath="$env:APPDATA\SCP Secret Laboratory\LabAPI\plugins\7777"
```

Observed: all three projects built with 0 warnings and 0 errors.

The 19 tracked files under `src/ToyTricksDemo` were compared with local ToyTricksDemo commit
`792696a9c5392b1961f977d0d645b1fb46e122ce` by SHA-256. All matched.

## Live role integration

1. Copied `Example.CharacterRoleProvider.dll` and `Example.CharacterRoleConsumer.dll` to the port
   7777 LabAPI plugin directory.
2. Compared each deployed DLL with its Release output by SHA-256. Both matched.
3. Restarted the visible port 7777 LocalAdmin server.
4. Ran the native `version` console command through LocalAdmin's same-user command channel.
5. Confirmed both plugins appeared in build info at version 1.0.0, with the Highest-priority provider
   listed before the Lowest-priority consumer.
6. Spawned a uniquely named native RA dummy with `dummies spawn RoleProbe`.
7. Changed it with `forcerole RoleProbe FacilityGuard`.

Expected log:

```text
[RoleConsumerExample] Detected RoleProbe as 'example.instructor' from the provider plugin.
```

Observed: the exact expected log appeared, followed by the native success response confirming that one
player changed to Facility Guard.

Cleanup: `dummies destroy RoleProbe` affected one dummy. The visible test server remained running.


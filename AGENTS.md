# SCPSL Plugin Examples

Read the [shared workspace instructions](../AGENTS.md) before working here, including
language, authorization and verification policy; independent repositories may not inherit them.
Use [plugin conventions](../docs/plugin-conventions.md) for shared settings, audio and UI.

- This repository teaches LabAPI plugin development through small, buildable examples.
- `src/ToyTricksDemo` is a complete source copy of the local ToyTricksDemo reference plugin.
- Keep `examples/CrossPluginRoles` intentionally small: one provider, one consumer, and one public query surface.
- Keep each loadable plugin in its own project and assembly.
- Build all projects before completion; live-check gameplay-facing changes on a local test server when practical.


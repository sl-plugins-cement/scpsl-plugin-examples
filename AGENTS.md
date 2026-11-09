# SCPSL Plugin Examples

Read the [shared workspace instructions](../AGENTS.md) ([GitHub](https://github.com/sl-plugins-cement/workspace-instructions/blob/main/AGENTS.md)) before working here, including
language, authorization and verification policy; independent repositories may not inherit them.
Use [plugin conventions](../docs/plugin-conventions.md) for shared settings, audio and UI.

- This repository teaches LabAPI plugin development through small, buildable examples.
- `src/ToyTricksDemo` is a complete source copy of the local ToyTricksDemo reference plugin.
- Keep `examples/CrossPluginRoles` intentionally small: one provider, one consumer, and one public query surface.
- Keep each loadable plugin in its own project and assembly.
- `examples/Foundations` demonstrates HSM, ServerKeybinds and CustomItems together; run
  `scripts/build-foundations.ps1` before building it.
- Dependency commits are pinned in `dependencies.json`; do not switch to a maintainer's hidden local
  dependencies. Check the preview rules with `node preview/check.cjs`.
- The foundations course, its collaboration notes and teaching hints are in Simplified Chinese; keep the
  existing English documentation in English.
- Build all projects before completion; live-check gameplay-facing changes on a local test server when practical.


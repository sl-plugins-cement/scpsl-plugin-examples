# SCPSL Plugin Examples

- This repository teaches LabAPI plugin development through small, buildable examples.
- `src/ToyTricksDemo` is a complete source copy of the local ToyTricksDemo reference plugin.
- Keep `examples/CrossPluginRoles` intentionally small: one provider, one consumer, and one public query surface.
- Keep each loadable plugin in its own project and assembly.
- 本批新增的基础课程、协作说明和教学提示使用简体中文；保留已有英文文档。修改实际产品的双语界面时继续维护两种语言。
- `examples/Foundations` 集中演示 HSM、ServerKeybinds 与 CustomItems；构建前运行 `scripts/build-foundations.ps1`。
- 依赖提交在 `dependencies.json` 锁定；不要改用维护者本机的隐藏依赖。预览规则用 `node preview/check.cjs` 检查。
- Build all projects before completion; live-check gameplay-facing changes on a local test server when practical.


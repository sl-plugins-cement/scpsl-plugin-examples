# 基础课程验证记录

日期：2026-09-19。范围：本分支的中文教程、构建流程、基础示例与浏览器预览。

| 检查 | 结果 |
| --- | --- |
| Windows PowerShell 从空 `.workspace` 克隆组织仓库并构建 | 通过；依赖提交按 `dependencies.json`，没有使用本机已安装的库 |
| Cement HSM 源码测试 | 184 通过，31 跳过，0 失败 |
| HSM 来源 | 从组织仓库克隆锁定提交并构建，输出版本 `5.5.1-cement.1` |
| 依赖目录含本地未提交文件 | 正确拒绝；文件内容和 HEAD 保持不变 |
| 四个项目的 Release 解决方案构建 | 通过，指定 HSM 构建输出的 Harmony 路径后 0 警告、0 错误 |
| ServerKeybinds 现有单元检查 | 17/17 通过 |
| 三种预览输入的静态布局检查 | 3/3 通过 |
| 直接打开 HTML 并切换三种样式 | Hint 数量正确，无浏览器脚本错误 |
| 画廊对公告/状态栏背景的预览 | 无已标记区域碰撞，图片已人工查看 |
| 冷却对背包背景的预览 | 无已标记区域碰撞，图片已人工查看 |
| 文档相对链接与 Git 空白检查 | 通过 |

ServerKeybinds 首次完整编译仍有原有 `KeybindBlock.cs:95` 的 CS8625 可空性警告；本次没有修改该接口。
画廊在背包打开时会遮住轮盘，因此教程要求关闭背包观看；不能把临时画廊坐标用于常驻 HUD。

**本次未部署到游戏服务器，未执行真实客户端按键、物品转交、角色/断线清理或 HSM 实际渲染验收。**
这些项目仍待按 [验收清单](foundations-verification.md) 执行，不计为通过。Cement HSM 与适配器的运行时兼容性也待加载验证。
本分支作为可构建的教学候选版交付，不作为已通过游戏验收的生产发布。

复现：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-foundations.ps1
dotnet build ScpslPluginExamples.sln -c Release -p:LabApiGlobalDependenciesPath="$PWD\.dependencies" -p:DeployToLocalServer=false
dotnet run --project .workspace/ServerKeybinds/tests/MusicPreferences.Unit/MusicPreferences.Unit.csproj -c Release
node preview/check.cjs
```

# 样式预览

双击 `index.html`，无需安装 Node 或 Web 服务。可选择自己的截图作为本地背景。

修改 `styles.json` 后运行：

```powershell
node preview/check.cjs --sync
```

该命令同步 `styles.js` 并检查定位规则，不证明真实客户端渲染正确。
`preview-core.js` 取自维护工作区 `.tests/UI/src/preview-core.js`，保留原渲染逻辑；
导入时 SHA-256：`095126081376E4333C9DB90C610C1B6213CFCE966A75EE3D8943106911842FD1`。
更新该副本时应同时检查游戏内校准与教程，不单独调整数学系数来让预览看起来正确。

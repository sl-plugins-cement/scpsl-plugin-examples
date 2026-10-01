const fs = require('node:fs');
const path = require('node:path');
const core = require('./preview-core.js');
const styles = JSON.parse(fs.readFileSync(path.join(__dirname, 'styles.json'), 'utf8'));
const expected = 'const cementStyles = ' + JSON.stringify(styles, null, 2) + ';\n';
if (process.argv.includes('--sync')) fs.writeFileSync(path.join(__dirname, 'styles.js'), expected);
if (fs.readFileSync(path.join(__dirname, 'styles.js'), 'utf8') !== expected) throw Error('styles.js 与 styles.json 不一致；运行 node preview/check.cjs --sync。');
let errors = 0;
for (const style of styles) {
  const model = core.buildPreviewModel(style.entries, { width: 1920, height: 1080 });
  console.log(JSON.stringify({ 样式: style.name, 检查: model.validation }));
  // 包括警告：教学样式不应依赖越界、Bottom 或左右对齐的特殊情况。
  if (!Array.isArray(model.validation)) throw Error('预览器返回了未知的检查结果结构。');
  errors += model.validation.filter(issue => issue.severity !== 'ok').length;
}
if (errors) process.exitCode = 1;

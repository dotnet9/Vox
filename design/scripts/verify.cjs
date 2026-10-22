/* Vox 原型校验：JS 语法、HTML 资源引用完整性、生成令牌的版本戳一致性、Logo 品牌色。
 * 通用部分沿用 Vex 的 verify 设计；Vex 应用专属断言（排版目录、偏好项）不适用于 Vox，已移除。 */
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
require('./sync.cjs')();
let scripts = 0;
function scan(folder) {
  for (const entry of fs.readdirSync(folder, {withFileTypes:true})) {
    if (entry.name === 'vendor' || entry.name === 'node_modules' || entry.name === 'fonts') continue;
    const file = path.join(folder, entry.name);
    if (entry.isDirectory()) scan(file);
    else if (/\.(js|cjs)$/.test(file)) {
      new vm.Script(fs.readFileSync(file, 'utf8'), {filename:file});
      scripts++;
    }
  }
}
scan(root);
for (const file of fs.readdirSync(root).filter(file => file.endsWith('.html'))) {
  const html = fs.readFileSync(path.join(root, file), 'utf8');
  for (const match of html.matchAll(/(?:src|href)="([^"?#]+)[^"]*"/g)) {
    if (/^(?:https?:|data:|mailto:|#)/.test(match[1])) continue;
    assert.ok(fs.existsSync(path.resolve(root, match[1])), `${file}: ${match[1]}`);
  }
}
const logo = fs.readFileSync(path.join(root, 'assets/vox-logo.svg'), 'utf8');
assert.ok(logo.includes('#4338CA'), 'Vox logo keeps the brand accent');
const sandbox = {window:{}};
vm.runInNewContext(fs.readFileSync(path.join(root, 'assets/generated/palette-rules-data.js'), 'utf8'), sandbox);
assert.ok(Number.isInteger(sandbox.window.VoxPaletteRulesData.ruleVersion), 'Palette data carries a ruleVersion stamp');
const paletteStamp = /\(ruleVersion (\d+)\)/.exec(fs.readFileSync(path.join(root, 'assets/palettes.css'), 'utf8'));
assert.ok(paletteStamp, 'palettes.css is generated with a ruleVersion stamp');
for (const generated of ['assets/palettes.css','assets/palettes.js','assets/density.css','assets/generated/palette-rules-data.js']) {
  const header = fs.readFileSync(path.join(root, generated), 'utf8').split('\n', 1)[0];
  assert.match(header, new RegExp(`ruleVersion ${sandbox.window.VoxPaletteRulesData.ruleVersion}`), `${generated} matches the generated ruleVersion`);
}
console.log(`Vox prototype verified: ${scripts} scripts, tokens at ruleVersion ${sandbox.window.VoxPaletteRulesData.ruleVersion}.`);

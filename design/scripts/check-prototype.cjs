// 原型门禁：视图清单一致性、引用资产存在性、离线自治。零第三方依赖。
// 用法: node design/scripts/check-prototype.cjs   （在仓库根或 design/scripts 下运行）
const fs = require('node:fs');
const path = require('node:path');
const designDir = path.resolve(__dirname, '..');
const failures = [];
let checks = 0;

for (const file of fs.readdirSync(designDir).filter(f => f.endsWith('.html'))) {
  const fullPath = path.join(designDir, file);
  const html = fs.readFileSync(fullPath, 'utf8');

  // 1. 引用资产必须存在；禁止网络引用（离线自治）。
  for (const match of html.matchAll(/(?:src|href)="([^"?#]+)[^"]*"/g)) {
    const ref = match[1];
    checks++;
    if (/^(?:javascript:|mailto:|data:)/i.test(ref)) continue;
        if (/^(?:https?:)?\/\//i.test(ref)) failures.push(`${file}: 网络引用违反离线自治 — ${ref}`);
    else if (!fs.existsSync(path.resolve(designDir, ref))) failures.push(`${file}: 引用资产不存在 — ${ref}`);
  }

  // 2. 视图清单一致性：data-view 与 titles 映射必须互为镜像（抓死键/漏键）。
  const views = [...html.matchAll(/data-view="([^"]+)"/g)].map(m => m[1]);
  const titlesMatch = html.match(/titles=\{([^}]*)\}/);
  if (titlesMatch) {
    const titleKeys = [...titlesMatch[1].matchAll(/([a-zA-Z]+):\[/g)].map(m => m[1]);
    checks++;
    for (const v of new Set(views))
      if (!titleKeys.includes(v)) failures.push(`${file}: data-view "${v}" 没有 titles 条目`);
    for (const k of new Set(titleKeys))
      if (!views.includes(k)) failures.push(`${file}: titles 键 "${k}" 没有对应视图（7→5 页改版类死数据）`);
    checks++;
  }
}
assertCount();
if (failures.length) {
  console.error(`原型门禁未通过（${failures.length} 项）：`);
  for (const f of failures) console.error('  ' + f);
  process.exit(1);
}
console.log(`PASS: 原型门禁通过（${checks} 项检查）`);

function assertCount() {
  if (checks === 0) { console.error('未发现任何可检查的 HTML'); process.exit(2); }
}

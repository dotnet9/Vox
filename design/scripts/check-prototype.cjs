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
    else {
      if (ref.startsWith('../')) failures.push(`${file}: 引用越出 design 根（file:// 可用但 http-server design 为根时 404）— ${ref}`);
      if (!fs.existsSync(path.resolve(designDir, ref))) failures.push(`${file}: 引用资产不存在 — ${ref}`);
    }
  }
  // 1b. CSS 里的 url(...) 同样禁止越根（防 file:// 习惯写法回流）。
  for (const cssFile of ['assets/tokens.css', 'assets/app.css', 'assets/installer.css']) {
    const css = fs.readFileSync(path.join(designDir, cssFile), 'utf8');
    for (const m of css.matchAll(/url\((?:'|")?([^'")]+)(?:'|"?)\)/g)) {
      checks++;
      if (m[1].startsWith('../')) failures.push(`${cssFile}: url() 引用越出 design 根 — ${m[1]}`);
    }
  }

  // 2. 视图清单一致性：data-view 与 titles 映射必须互为镜像（抓死键/漏键）。
  const views = [...html.matchAll(/data-view="([^"]+)"/g)].map(m => m[1]);
  const titlesMatch = html.match(/titles=\{([^}]*)\}/);
  if (titlesMatch) {
    const titleEntries = [...titlesMatch[1].matchAll(/([a-zA-Z]+):\[((?:[^\]\\]|\\.)*)\]/g)];
    const titleKeys = titleEntries.map(m => m[1]);
    checks++;
    for (const v of new Set(views))
      if (!titleKeys.includes(v)) failures.push(`${file}: data-view "${v}" 没有 titles 条目`);
    for (const k of new Set(titleKeys))
      if (!views.includes(k)) failures.push(`${file}: titles 键 "${k}" 没有对应视图（7→5 页改版类死数据）`);
    checks++;

    // 3. 数据口径一致性：titles 文案里的「N 篇」必须与同导航项 .cnt 徽标数字一致（防 37/536 漂移）。
    const navCnt = {};
    for (const line of html.split('\n')) {
      const v = line.match(/data-view="(\w+)"/);
      const c = line.match(/class="cnt"[^>]*>(\d+)</);
      if (v && c) navCnt[v[1]] = c[1];
    }
    checks++;
    for (const [, key, label] of titleEntries) {
      const n = label.match(/(\d+)\s*篇/);
      if (n && navCnt[key] && navCnt[key] !== n[1])
        failures.push(`${file}: titles「${key}」篇数 ${n[1]} 与导航徽标 ${navCnt[key]} 不一致`);
    }
  }

  // 4. onclick 引用的自定义函数必须已定义（抓拼写错误 / 漏定义）。
  const definedNames = new Set();
  for (const m of html.matchAll(/function\s+([A-Za-z_$][\w$]*)\s*\(/g)) definedNames.add(m[1]);
  for (const m of html.matchAll(/(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=/g)) definedNames.add(m[1]);
  for (const m of html.matchAll(/window\.([A-Za-z_$][\w$]*)\s*=/g)) definedNames.add(m[1]);
  const jsBuiltins = new Set(['document', 'window', 'confirm', 'alert', 'navigator', 'event', 'parseInt', 'parseFloat', 'String', 'Number', 'Boolean', 'var', 'if', 'for', 'while', 'return']);
  let onclickCalls = 0;
  for (const m of html.matchAll(/onclick="([^"]+)"/g)) {
    for (const c of m[1].matchAll(/([A-Za-z_$][\w$]*)\s*\(/g)) {
      if (m[1][c.index - 1] === '.') continue; // 方法调用（如 document.getElementById）
      const fn = c[1];
      if (jsBuiltins.has(fn)) continue;
      checks++;
      onclickCalls++;
      if (!definedNames.has(fn)) failures.push(`${file}: onclick 引用了未定义的函数 — ${fn}()`);
    }
  }
}

// 5. 令牌对比度矩阵（WCAG）：八强调色 × 明暗，ink 徽章文字、on-accent、accent-deep 必须达标。
//    口径：徽章 ink 落同色 soft 底 ≥4.5；on-accent 落 accent（静止态）≥4.5；accent-deep 落 accent-soft/surface-3 ≥4.5；
//    on-ok/on-warn 落对应语义填充 ≥4.5。
{
  const css = fs.readFileSync(path.join(designDir, 'assets/tokens.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
  const parse = value => {
    let hex = /^#([0-9a-f]{6})$/i.exec(value.trim());
    if (hex) { const n = parseInt(hex[1], 16); return [n >> 16 & 255, n >> 8 & 255, n & 255, 1]; }
    hex = /^#([0-9a-f]{3})$/i.exec(value.trim());
    if (hex) { const n = hex[1]; return [...n.split('').map(c => parseInt(c + c, 16)), 1]; }
    const rgb = /^rgba?\(([^)]+)\)$/.exec(value.trim());
    if (rgb) { const p = rgb[1].split(/[\s,\/]+/).filter(Boolean).map(Number); return [p[0], p[1], p[2], p.length > 3 ? p[3] : 1]; }
    return null;
  };
  const over = (fg, bg) => fg[3] >= 1 ? fg : fg.slice(0, 3).map((v, i) => v * fg[3] + bg[i] * (1 - fg[3])).concat(1);
  const lum = c => c.slice(0, 3).map(v => { v /= 255; return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; })
    .reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0);
  const ratio = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
  const scopes = {};
  for (const m of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    const theme = /\[data-theme="(light|dark)"\]/.exec(m[1]);
    const accent = /\[data-accent="([a-z]+)"\]/.exec(m[1]);
    const key = (theme ? theme[1] : 'light') + '/' + (accent ? accent[1] : '');
    scopes[key] = Object.assign(scopes[key] || {}, Object.fromEntries(m[2].split(';').map(d => d.split(':')).filter(d => d.length === 2 && d[0].trim().startsWith('--')).map(d => [d[0].trim(), d[1].trim()])));
  }
  for (const [name, vars] of Object.entries(scopes)) {
    const theme = name.split('/')[0];
    if (!vars['--accent']) continue;
    const white = theme === 'light' ? [255, 255, 255, 1] : [9, 9, 11, 1];
    const pair = (fgKey, bgKey, min) => {
      if (!vars[fgKey] || !vars[bgKey]) return;
      const fg = over(parse(vars[fgKey]), white);
      const bg = over(parse(vars[bgKey]), white);
      const r = ratio(fg, bg);
      if (r < min) failures.push(`tokens.css ${name}: ${fgKey} on ${bgKey} = ${r.toFixed(2)}:1（需 ≥${min}）`);
    };
    checks++;
    for (const ink of ['--ok-ink', '--warn-ink', '--danger-ink']) pair(ink, ink.replace('-ink', '-soft'), 4.5);
    pair('--on-accent', '--accent', 4.5);
    pair('--accent-deep', '--accent-soft', 4.5);
    pair('--accent-deep', '--surface-3', 4.5);
    pair('--on-ok', '--ok', 4.5);
    pair('--on-warn', '--warn', 4.5);
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

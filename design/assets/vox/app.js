/* Vox 原型共享脚本：外壳注入、明暗/配色/密度持久化、模拟数据与页面渲染。
 * 仅消费生成的令牌数据（VoxPalettes / VoxPaletteRulesData），演示数据为虚构。 */
(function () {
  'use strict';

  /* ---------- 存储（?preview=1 时使用内存，供 iframe 预览） ---------- */
  const params = new URLSearchParams(location.search);
  const memory = new Map();
  const VoxStorage = {
    getItem(k) { if (params.get('preview') === '1') return memory.get(k) || null; try { return localStorage.getItem(k); } catch { return null; } },
    setItem(k, v) { if (params.get('preview') === '1') { memory.set(k, v); return; } try { localStorage.setItem(k, v); } catch { } },
    removeItem(k) { if (params.get('preview') === '1') { memory.delete(k); return; } try { localStorage.removeItem(k); } catch { } }
  };

  /* ---------- 图标 ---------- */
  const ICONS = {
    home: '<rect x="3" y="10" width="18" height="12" rx="2"/><path d="m5 10 7-7 7 7"/>',
    articles: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="8" y1="13" x2="16" y2="13"/><line x1="8" y1="17" x2="13" y2="17"/>',
    audit: '<path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>',
    publish: '<path d="m22 2-7 20-4-9-9-4z"/><path d="M22 2 11 13"/>',
    appear: '<path d="M12 3a9 9 0 0 0 0 18h1.5a2 2 0 0 0 0-4H12a2 2 0 0 1 0-4h3.5a5.5 5.5 0 0 0 0-11H12Z"/><circle cx="7.5" cy="9" r="1"/><circle cx="11" cy="6.5" r="1"/><circle cx="16" cy="7.5" r="1"/>',
    search: '<circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>',
    file: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>',
    image: '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8" cy="9" r="1.5"/><path d="m3 16 5-5 4 4 3-3 6 6"/>',
    tag: '<path d="M20.59 13.41 12 22l-9-9V4a1 1 0 0 1 1-1h9z"/><circle cx="7.5" cy="7.5" r="1.5"/>',
    check: '<polyline points="20 6 9 17 4 12"/>',
    warn: '<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>',
    info: '<circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>',
    copy: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
    clipboard: '<rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/>',
    ext: '<path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/>',
    edit: '<path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4z"/>',
    trash: '<polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>',
    refresh: '<polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    chevron: '<path d="m9 5 7 7-7 7"/>',
    chevronDown: '<path d="m6 9 6 6 6-6"/>',
    x: '<line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/>',
    min: '<line x1="5" y1="12" x2="19" y2="12"/>',
    max: '<rect x="5" y="5" width="14" height="14" rx="2"/>',
    eye: '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>',
    code: '<path d="m8 8-4 4 4 4M16 8l4 4-4 4"/>',
    save: '<path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/>',
    send: '<path d="m22 2-7 20-4-9-9-4z"/><path d="M22 2 11 13"/>',
    git: '<circle cx="6" cy="6" r="3"/><circle cx="18" cy="18" r="3"/><path d="M6 9v3a3 3 0 0 0 3 3h6"/>',
    folder: '<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>'
  };
  function icon(name, cls) { return '<svg class="ico ' + (cls || '') + '" aria-hidden="true"><use href="#i-' + name + '"/></svg>'; }

  function injectSprite() {
    const symbols = Object.entries(ICONS).map(([k, d]) => '<symbol id="i-' + k + '" viewBox="0 0 24 24">' + d + '</symbol>').join('');
    const svg = '<svg xmlns="http://www.w3.org/2000/svg" style="display:none"><defs>' + symbols + '</defs></svg>';
    document.body.insertAdjacentHTML('afterbegin', svg);
  }

  /* ---------- 外观设置 ---------- */
  const settings = {
    theme: VoxStorage.getItem('vox-theme') || 'light',
    palette: VoxStorage.getItem('vox-palette') || 'indigo',
    density: VoxStorage.getItem('vox-density') || 'normal'
  };
  if (params.get('theme')) settings.theme = params.get('theme');
  if (params.get('palette')) settings.palette = params.get('palette');
  if (params.get('density')) settings.density = params.get('density');
  function applySettings(persist) {
    const root = document.documentElement;
    root.dataset.theme = settings.theme;
    root.dataset.palette = settings.palette;
    root.dataset.density = settings.density;
    document.querySelectorAll('[data-set-theme]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.setTheme === settings.theme)));
    document.querySelectorAll('[data-set-palette]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.setPalette === settings.palette)));
    document.querySelectorAll('[data-set-density]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.setDensity === settings.density)));
    if (persist) {
      VoxStorage.setItem('vox-theme', settings.theme);
      VoxStorage.setItem('vox-palette', settings.palette);
      VoxStorage.setItem('vox-density', settings.density);
    }
  }

  /* ---------- 模拟数据（虚构，仅用于原型评审） ---------- */
  const REPO = 'D:\\github\\apps\\Assets.Dotnet9';
  const DATA = {
    stats: { articles: 550, images: 5383, words: '128.4 万', monthNew: 6 },
    years: [['2019', 12], ['2020', 38], ['2021', 45], ['2022', 52], ['2023', 78], ['2024', 96], ['2025', 190], ['2026', 39]],
    categories: [['.NET', 186], ['C#', 132], ['WPF', 97], ['Avalonia UI', 54], ['Blazor', 41], ['工程效率', 40]],
    albums: [
      { name: 'C#开源项目', total: 45, published: 41 },
      { name: 'WPF UI设计', total: 32, published: 30 },
      { name: 'Avalonia 跨平台实战', total: 24, published: 19 },
      { name: '从护士到C#开发者', total: 18, published: 18 },
      { name: 'AI 工作流', total: 12, published: 9 }
    ],
    articles: [
      { id: 1, title: 'Avalonia 12 桌面应用开发实战：从零到发布', slug: 'avalonia-12-desktop-practices', path: '2026/10', date: '2026-10-18', words: 8412, cat: 'Avalonia UI', album: 'Avalonia 跨平台实战', tags: ['Avalonia', 'C#'], cover: true, draft: false, stale: true, author: 'Dotnet9' },
      { id: 2, title: '一份 Markdown，多发全平台的工程实践', slug: 'markdown-publishing-pipeline', path: '2026/04', date: '2026-04-02', words: 5600, cat: '工程效率', album: 'AI 工作流', tags: ['Markdown', '自媒体'], cover: true, draft: false, stale: false, author: 'Dotnet9' },
      { id: 3, title: '微信小程序的 .NET 后端实践', slug: 'wechat-miniprogram-dotnet-backend', path: '2026/09', date: '2026-09-02', words: 6200, cat: '.NET', album: '', tags: ['微信', 'ASP.NET Core'], cover: true, draft: false, stale: false, author: 'Dotnet9' },
      { id: 4, title: 'Native AOT 性能实测笔记', slug: 'native-aot-performance-notes', path: '2026/08', date: '2026-08-21', words: 4800, cat: '.NET', album: '', tags: ['Native AOT'], cover: true, draft: false, stale: false, author: 'Dotnet9' },
      { id: 5, title: '2026 上半年 C# 开源项目精选', slug: 'csharp-open-source-projects-2026', path: '2026/07', date: '2026-07-15', words: 5200, cat: 'C#', album: 'C#开源项目', tags: ['开源'], cover: true, draft: false, stale: false, author: 'Dotnet9' },
      { id: 6, title: 'WPF UI 设计精选（九）', slug: 'wpf-ui-design-collection-9', path: '2026/05', date: '2026-05-30', words: 3900, cat: 'WPF', album: 'WPF UI设计', tags: ['WPF', 'UI'], cover: false, draft: false, stale: false, author: 'Dotnet9' },
      { id: 7, title: 'Semantic Kernel RAG 入门', slug: 'semantic-kernel-rag-getting-started', path: '2026/04', date: '2026-04-12', words: 7400, cat: '.NET', album: 'AI 工作流', tags: ['Semantic Kernel', 'RAG'], cover: true, draft: true, stale: false, author: 'Dotnet9' },
      { id: 8, title: 'ReactiveUI 在 Avalonia 中的绑定姿势', slug: 'reactiveui-avalonia-bindings', path: '2025/12', date: '2025-12-20', words: 4400, cat: 'Avalonia UI', album: 'Avalonia 跨平台实战', tags: ['ReactiveUI'], cover: true, draft: false, stale: false, author: 'Dotnet9' },
      { id: 9, title: 'Prism.DryIoc 模块化加载详解', slug: 'prism-dryioc-module-loading', path: '2025/11', date: '2025-11-08', words: 6100, cat: '.NET', album: '', tags: ['Prism'], cover: true, draft: false, stale: false, author: 'Dotnet9' },
      { id: 10, title: 'Blazor Hybrid 桌面方案踩坑记', slug: 'blazor-hybrid-desktop', path: '2025/08', date: '2025-08-01', words: 6800, cat: 'Blazor', album: '', tags: ['Blazor'], cover: false, draft: false, stale: false, author: 'Dotnet9' },
      { id: 11, title: 'EF Core 拦截器实现操作审计', slug: 'efcore-interceptor-audit', path: '2025/06', date: '2025-06-22', words: 4700, cat: '.NET', album: '', tags: ['EF Core'], cover: true, draft: false, stale: false, author: 'Dotnet9' },
      { id: 12, title: '从 MyBatis 到 Dapper 的迁移手记', slug: 'mybatis-to-dapper-migration', path: '2025/03', date: '2025-03-10', words: 4100, cat: '.NET', album: '', tags: ['Dapper'], cover: true, draft: true, stale: false, author: 'Dotnet9' },
      { id: 13, title: 'C# Source Generator 入门指南', slug: 'csharp-source-generator-guide', path: '2024/12', date: '2024-12-05', words: 7200, cat: 'C#', album: '', tags: ['Source Generator'], cover: false, draft: false, stale: false, author: 'Dotnet9' },
      { id: 14, title: 'WPF 文件拖拽的正确姿势', slug: 'WPF-file-drag', path: '2020/11', date: '2020-11-16', words: 2100, cat: 'WPF', album: 'WPF UI设计', tags: ['WPF'], cover: true, draft: false, stale: false, author: 'Dotnet9' }
    ],
    audit: [
      { rule: 'draft', name: 'draft 未发布', sev: 'info', icon: 'info', items: [7, 12] },
      { rule: 'missing-cover', name: '缺封面', sev: 'warn', icon: 'image', items: [6, 10, 13] },
      { rule: 'missing-description', name: '缺摘要 description', sev: 'warn', icon: 'file', items: [9, 12] },
      { rule: 'slug', name: 'slug 不合规（大小写 / 中文混杂）', sev: 'error', icon: 'warn', items: [14] },
      { rule: 'broken-image', name: '图片死链', sev: 'error', icon: 'image', items: [5, 10] },
      { rule: 'missing-lastmod', name: '缺 lastmod', sev: 'info', icon: 'refresh', items: [5, 2, 13] }
    ],
    platforms: [
      { name: '博客园', mode: 'api', status: 'ok', info: '10-18 已发布 · cnblogs.com/Dotnet9-com' },
      { name: 'CSDN', mode: 'api', status: 'stale', info: '10-15 发布 · 源文件 10-20 有改动' },
      { name: '微信公众号', mode: 'api', status: 'ok', info: '10-18 已发布 · 已传素材库 12 图' },
      { name: '掘金', mode: 'unofficial', status: 'ok', info: '10-18 已发布' },
      { name: '今日头条', mode: 'unofficial', status: 'ok', info: '10-19 已发布' },
      { name: 'B 站专栏', mode: 'unofficial', status: 'todo', info: '未发布' },
      { name: 'InfoQ 写作社区', mode: 'unofficial', status: 'todo', info: '未发布' },
      { name: '腾讯云开发者社区', mode: 'unofficial', status: 'ok', info: '10-18 已发布' },
      { name: '阿里云开发者社区', mode: 'unofficial', status: 'todo', info: '未发布' },
      { name: '知乎', mode: 'copy', status: 'todo', info: '未发布 · 复制助手' },
      { name: '简书', mode: 'copy', status: 'todo', info: '未发布 · 复制助手' },
      { name: '51CTO 博客', mode: 'copy', status: 'ok', info: '10-19 粘贴完成' },
      { name: '华为云开发者社区', mode: 'copy', status: 'todo', info: '未发布 · 复制助手' },
      { name: 'Boss 有乐', mode: 'copy', status: 'todo', info: '未发布 · 复制助手' },
      { name: 'GitHub · Gitee', mode: 'git', status: 'ok', info: '10-20 已同步镜像仓库' }
    ],
    modeLabels: { api: 'API 直发', unofficial: '非官方接口', copy: '复制助手', git: 'git 镜像' },
    statusLabels: { ok: ['已发布', 'ok'], stale: ['需更新', 'stale'], todo: ['未发布', 'todo'], error: ['失败', 'error'] },
    floatFields: [
      { k: '标题', field: 'title', count: '18/100' },
      { k: '话题', field: 'tags', count: '5 个' },
      { k: '封面', field: 'cover', count: '已选' },
      { k: '摘要', field: 'digest', count: '74/120' },
      { k: '正文', field: 'body', count: '8,412 字 · 富文本' }
    ]
  };

  const featured = DATA.articles[0];
  const FLOAT_VALUES = {
    title: featured.title,
    tags: 'Avalonia · C# · 跨平台 · 桌面开发 · 实战',
    cover: 'img1.dotnet9.com/2026/10/avalonia12-cover.png',
    digest: '从项目创建到三平台打包发布，一篇讲透 Avalonia 12 桌面应用开发的完整链路与踩坑点。',
    body: '## 一、为什么是 Avalonia 12\n\n（原型演示：此处为平台适配后的富文本 HTML…）'
  };

  /* ---------- 工具 ---------- */
  function fmt(n) { return Number(n).toLocaleString('zh-CN'); }
  function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
  function articleById(id) { return DATA.articles.find(a => a.id === Number(id)); }
  function statusPill(s) { const [label, cls] = DATA.statusLabels[s]; return '<span class="pill-status ' + cls + '"><i></i>' + label + '</span>'; }
  function modeBadge(m) { return '<span class="badge ' + m + '">' + DATA.modeLabels[m] + '</span>'; }

  let toastTimer = null;
  function toast(message) {
    let box = document.getElementById('vox-toast');
    if (!box) {
      box = document.createElement('div');
      box.id = 'vox-toast';
      box.style.cssText = 'position:fixed;left:50%;bottom:56px;transform:translateX(-50%);background:var(--tooltip-bg);color:var(--tooltip-fg);font-size:12px;padding:8px 16px;border-radius:99px;z-index:99;box-shadow:var(--pop-shadow)';
      document.body.appendChild(box);
    }
    box.textContent = message;
    box.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { box.hidden = true; }, 1800);
  }
  function copyText(text, message) {
    const finish = () => toast(message || '已复制（原型演示）');
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(finish, finish);
    else finish();
  }

  /* ---------- 外壳注入 ---------- */
  const NAV = [
    { page: 'dashboard', href: 'Vox.html', icon: 'home', label: '概览' },
    { page: 'articles', href: 'Articles.html', icon: 'articles', label: '文章', count: '550' },
    { page: 'audit', href: 'Audit.html', icon: 'audit', label: '规范体检', count: '13', warn: true },
    { page: 'publishing', href: 'Publishing.html', icon: 'publish', label: '发布清单' },
    { page: 'appearance', href: 'Appearance.html', icon: 'appear', label: '外观' }
  ];
  const PAGE_TITLES = {
    dashboard: '概览 · Assets.Dotnet9', articles: '文章管理 · 550 篇', detail: 'Avalonia 12 桌面应用开发实战…',
    audit: '规范体检 · 13 项待处理', publishing: '发布清单 · Avalonia 12 桌面应用开发实战', appearance: '外观设置'
  };
  function buildShell() {
    const page = document.body.dataset.page;
    const tb = document.getElementById('tb');
    if (tb) tb.innerHTML =
      '<div class="brand"><img src="assets/vox-logo.svg" alt="">Vox <small>· 维声</small></div>' +
      '<div class="tb-doc"><span>' + esc(PAGE_TITLES[page] || 'Vox') + '</span></div>' +
      '<div class="caption-btns">' +
      '<button class="caption-btn" type="button" title="最小化（演示）">' + icon('min') + '</button>' +
      '<button class="caption-btn" type="button" title="最大化（演示）">' + icon('max') + '</button>' +
      '<button class="caption-btn close" type="button" title="关闭（演示）">' + icon('x') + '</button></div>';
    const nav = document.getElementById('nav');
    if (nav) nav.innerHTML =
      '<div class="nav-inner"><div class="nav-label">内容运营</div>' +
      NAV.filter(n => n.page !== 'appearance').map(n =>
        '<a class="nav-item" href="' + n.href + '"' + (n.page === page ? ' aria-current="page"' : '') + '>' + icon(n.icon) + n.label +
        (n.count ? '<span class="count' + (n.warn ? ' warn' : '') + '">' + n.count + '</span>' : '') + '</a>').join('') +
      '<div class="nav-label">偏好</div>' +
      '<a class="nav-item" href="Appearance.html"' + (page === 'appearance' ? ' aria-current="page"' : '') + '>' + icon('appear') + '外观</a>' +
      '<div class="mode-row" style="margin-top:10px">' +
      '<button class="btn sm" data-set-theme="light">浅色</button><button class="btn sm" data-set-theme="dark">深色</button></div>' +
      '</div>' +
      '<div class="nav-foot"><b>Assets.Dotnet9</b><br>main · 550 篇 · 5,383 图<br>演示数据，未连接真实仓库</div>';
    const sb = document.getElementById('statusbar');
    if (sb) {
      const rule = window.VoxPaletteRulesData ? window.VoxPaletteRulesData.ruleVersion : '?';
      const ver = window.VoxProject ? window.VoxProject.version : '0.1.0';
      sb.innerHTML = '<span><span class="ok-dot"></span>仓库已连接 · ' + esc(REPO) + '（演示）</span>' +
        '<span class="right">Vox ' + ver + ' 原型 · 令牌 ruleVersion ' + rule + ' · 9 套配色 × 明暗 × 密度三档</span>';
    }
    document.addEventListener('click', e => {
      const themeBtn = e.target.closest('[data-set-theme]');
      if (themeBtn) { settings.theme = themeBtn.dataset.setTheme; applySettings(true); }
    });
  }

  /* ---------- 页面：概览 ---------- */
  function renderDashboard() {
    const max = Math.max(...DATA.years.map(y => y[1]));
    const el = id => document.getElementById(id);
    if (!el || !el('year-bars')) return;
    el('year-bars').innerHTML = DATA.years.map(([y, n]) =>
      '<div class="bar-row"><span>' + y + '</span><span class="bar"><i style="width:' + Math.round(n / max * 100) + '%"></i></span><span class="val">' + n + '</span></div>').join('');
    const maxCat = DATA.categories[0][1];
    el('cat-bars').innerHTML = DATA.categories.map(([c, n]) =>
      '<div class="bar-row"><span>' + esc(c) + '</span><span class="bar"><i style="width:' + Math.round(n / maxCat * 100) + '%"></i></span><span class="val">' + n + '</span></div>').join('');
    el('albums').innerHTML = DATA.albums.map(a =>
      '<div class="album-row"><b>' + esc(a.name) + '</b><span>' + a.published + ' / ' + a.total + ' 篇已发布</span>' +
      '<span class="track"><i style="width:' + Math.round(a.published / a.total * 100) + '%"></i></span></div>').join('');
    const recent = DATA.articles.slice(0, 6);
    el('recent-body').innerHTML = recent.map(a =>
      '<tr><td class="title-cell"><b>' + esc(a.title) + '</b><small>' + a.path + '/' + a.slug + '.md</small></td>' +
      '<td>' + a.date + '</td><td>' + esc(a.cat) + '</td><td class="num">' + fmt(a.words) + '</td><td>' +
      (a.draft ? '<span class="badge">草稿</span>' : statusPill('ok')) + '</td>' +
      '<td><div class="row-actions"><a class="icon-btn" href="ArticleDetail.html" title="编辑">' + icon('edit') + '</a></div></td></tr>').join('');
    const issues = DATA.audit.reduce((n, g) => n + g.items.length, 0);
    el('audit-summary-body').innerHTML = DATA.audit.slice(0, 4).map(g =>
      '<div class="issue-item">' + icon(g.icon) + '<span>' + g.name + '</span>' +
      '<span class="slug">' + g.items.length + ' 篇</span><span class="fix"><a class="btn sm ghost" href="Audit.html">查看</a></span></div>').join('');
    el('audit-total').textContent = issues;
  }

  /* ---------- 页面：文章管理 ---------- */
  const articleFilters = { q: '', year: '', cat: '', status: 'all' };
  function renderArticleRows() {
    const tbody = document.getElementById('articles-body');
    if (!tbody) return;
    const rows = DATA.articles.filter(a =>
      (!articleFilters.q || (a.title + a.slug).toLowerCase().includes(articleFilters.q.toLowerCase())) &&
      (!articleFilters.year || a.path.startsWith(articleFilters.year)) &&
      (!articleFilters.cat || a.cat === articleFilters.cat) &&
      (articleFilters.status === 'all' || (articleFilters.status === 'draft' ? a.draft : !a.draft)));
    document.getElementById('articles-count').textContent = rows.length + ' / ' + DATA.articles.length + ' 篇';
    tbody.innerHTML = rows.map(a =>
      '<tr data-id="' + a.id + '"><td class="title-cell"><b>' + esc(a.title) + '</b><small>' + a.path + '/' + esc(a.slug) + '.md</small></td>' +
      '<td>' + a.date + '</td><td>' + esc(a.cat) + '</td><td>' + (a.album ? esc(a.album) : '<span class="muted">—</span>') + '</td>' +
      '<td>' + a.tags.map(t => '<span class="tag">' + esc(t) + '</span>').join('') + '</td>' +
      '<td class="num">' + fmt(a.words) + '</td><td>' + (a.draft ? '<span class="badge">草稿</span>' : (a.stale ? statusPill('stale') : statusPill('ok'))) + '</td>' +
      '<td><div class="row-actions"><a class="icon-btn" href="ArticleDetail.html" title="编辑">' + icon('edit') + '</a>' +
      '<button class="icon-btn" type="button" data-del="' + a.id + '" title="删除（演示）">' + icon('trash') + '</button></div></td></tr>').join('') ||
      '<tr><td colspan="8" style="text-align:center;padding:30px;color:var(--muted)">没有匹配的文章</td></tr>';
  }
  function bindArticles() {
    const q = document.getElementById('article-q');
    if (!q) return;
    q.addEventListener('input', () => { articleFilters.q = q.value; renderArticleRows(); });
    document.getElementById('article-year').addEventListener('change', e => { articleFilters.year = e.target.value; renderArticleRows(); });
    document.getElementById('article-cat').addEventListener('change', e => { articleFilters.cat = e.target.value; renderArticleRows(); });
    document.querySelectorAll('[data-status]').forEach(chip => chip.addEventListener('click', () => {
      articleFilters.status = chip.dataset.status;
      document.querySelectorAll('[data-status]').forEach(b => b.setAttribute('aria-pressed', String(b === chip)));
      renderArticleRows();
    }));
    document.getElementById('articles-body').addEventListener('click', e => {
      const del = e.target.closest('[data-del]');
      if (del) toast('删除需二次确认与 git 保护（原型演示）');
    });
    renderArticleRows();
  }

  /* ---------- 页面：文章详情 ---------- */
  function bindDetail() {
    const tabs = document.querySelectorAll('[data-tab]');
    if (!tabs.length) return;
    tabs.forEach(tab => tab.addEventListener('click', () => {
      tabs.forEach(t => t.setAttribute('aria-selected', String(t === tab)));
      document.getElementById('pane-preview').hidden = tab.dataset.tab !== 'preview';
      document.getElementById('pane-source').hidden = tab.dataset.tab !== 'source';
    }));
    const save = document.getElementById('detail-save');
    if (save) save.addEventListener('click', () => toast('保存将写入 slug.yml（原型演示）'));
  }

  /* ---------- 页面：规范体检 ---------- */
  function renderAudit() {
    const wrap = document.getElementById('audit-groups');
    if (!wrap) return;
    const total = DATA.audit.reduce((n, g) => n + g.items.length, 0);
    document.getElementById('audit-all').textContent = total;
    wrap.innerHTML = DATA.audit.map(g => {
      const sevIcon = g.sev === 'error' ? icon('warn') : g.sev === 'warn' ? icon('warn') : icon('info');
      const rows = g.items.map(id => {
        const a = articleById(id);
        return '<div class="issue-item">' + icon('file') +
          '<span style="min-width:0"><b>' + esc(a.title) + '</b><span class="slug"> ' + a.path + '/' + esc(a.slug) + '</span></span>' +
          '<span class="fix"><button class="btn sm ghost" type="button" data-fix="' + g.rule + '">修复</button></span></div>';
      }).join('');
      return '<section class="card" style="margin-bottom:14px"><div class="card-head"><h2>' + sevIcon + ' ' + g.name + '</h2>' +
        '<span class="badge">' + g.items.length + ' 篇</span></div>' + rows + '</section>';
    }).join('');
    wrap.addEventListener('click', e => {
      if (e.target.closest('[data-fix]')) toast('修复建议将写入 sidecar yml（原型演示）');
    });
  }

  /* ---------- 页面：发布清单 ---------- */
  function renderPublishing() {
    const tbody = document.getElementById('platform-body');
    if (!tbody) return;
    document.getElementById('pf-title').textContent = featured.title;
    document.getElementById('pf-meta').textContent = featured.path + '/' + featured.slug + '.md · ' + fmt(featured.words) + ' 字 · 2026-10-20 修改';
    tbody.innerHTML = DATA.platforms.map((p, i) =>
      '<tr><td><b>' + p.name + '</b></td><td>' + modeBadge(p.mode) + '</td><td>' + statusPill(p.status) + '</td>' +
      '<td class="muted" style="font-size:11px">' + p.info + '</td>' +
      '<td><div class="row-actions" style="opacity:1;justify-content:flex-end">' +
      (p.status === 'todo'
        ? '<button class="btn sm' + (p.mode === 'api' || p.mode === 'unofficial' ? ' primary' : '') + '" type="button" data-pub="' + i + '">' + (p.mode === 'copy' ? icon('clipboard') + '复制并打开' : icon('send') + '发布') + '</button>'
        : (p.status === 'stale'
          ? '<button class="btn sm primary" type="button" data-pub="' + i + '">' + icon('refresh') + '同步更新</button>'
          : '<button class="btn sm ghost" type="button" data-view="' + i + '">' + icon('ext') + '查看</button>')) +
      '</div></td></tr>').join('');
    tbody.addEventListener('click', e => {
      const pub = e.target.closest('[data-pub]');
      if (pub) {
        const p = DATA.platforms[Number(pub.dataset.pub)];
        if (p.mode === 'copy') { toggleFloat(true); toast('已复制「标题」，请粘贴到 ' + p.name + '（原型演示）'); }
        else toast('通过' + DATA.modeLabels[p.mode] + '发布到 ' + p.name + '（原型演示）');
      }
      const view = e.target.closest('[data-view]');
      if (view) toast('打开平台文章链接（原型演示）');
    });
    bindFloat();
  }

  /* ---------- 复制助手浮动面板 ---------- */
  let floatBound = false;
  function toggleFloat(show) {
    const panel = document.getElementById('float-panel');
    if (panel) panel.hidden = !show;
  }
  function bindFloat() {
    const panel = document.getElementById('float-panel');
    if (!panel || floatBound) return;
    floatBound = true;
    document.getElementById('float-open').addEventListener('click', () => toggleFloat(true));
    document.getElementById('float-close').addEventListener('click', () => toggleFloat(false));
    document.getElementById('float-fields').innerHTML = DATA.floatFields.map((f, i) =>
      '<div class="float-field"><span class="k"><b>' + f.k + '</b>' + f.count + '</span>' +
      '<span class="v">' + esc(String(FLOAT_VALUES[f.field]).slice(0, 40)) + '</span>' +
      '<button class="btn sm" type="button" data-copy="' + f.field + '">' + icon('copy') + '<kbd>' + (i + 1) + '</kbd></button></div>').join('');
    document.getElementById('float-fields').addEventListener('click', e => {
      const btn = e.target.closest('[data-copy]');
      if (btn) copyText(String(FLOAT_VALUES[btn.dataset.copy]), '已复制「' + DATA.floatFields.find(f => f.field === btn.dataset.copy).k + '」，请到平台页面粘贴');
    });
    document.addEventListener('keydown', e => {
      if (!e.altKey || e.ctrlKey || e.shiftKey) return;
      const n = Number(e.key);
      if (n >= 1 && n <= DATA.floatFields.length) {
        copyText(String(FLOAT_VALUES[DATA.floatFields[n - 1].field]), 'Alt+' + n + ' 已复制');
        e.preventDefault();
      }
    });
    document.getElementById('float-compose').addEventListener('click', () => toast('将打开知乎创作平台（原型演示）'));
    document.getElementById('float-done').addEventListener('click', () => { toggleFloat(false); toast('已记录发布状态，写入本地 SQLite（原型演示）'); });
  }

  /* ---------- 页面：外观 ---------- */
  function renderAppearance() {
    const grid = document.getElementById('palettes');
    if (!grid || !window.VoxPalettes) return;
    grid.innerHTML = VoxPalettes.map(p =>
      '<button class="palette-choice" type="button" data-set-palette="' + p.key + '">' +
      '<span class="swatch-dots">' + p.swatch.map(c => '<i style="background:' + c + '"></i>').join('') + '</span>' +
      '<span class="palette-label"><b>' + p.label + ' ' + p.en + '</b><small>' + p.note + '</small></span></button>').join('');
    document.querySelectorAll('[data-set-palette]').forEach(b => b.addEventListener('click', () => { settings.palette = b.dataset.setPalette; applySettings(true); }));
    document.querySelectorAll('[data-set-density]').forEach(b => b.addEventListener('click', () => { settings.density = b.dataset.setDensity; applySettings(true); }));
    document.querySelectorAll('[data-set-theme]').forEach(b => b.addEventListener('click', () => { settings.theme = b.dataset.setTheme; applySettings(true); }));

    /* 自定义配色：编写层 4 色 + 状态默认 → 规则引擎实时派生（与生成器共用 palette-rules.js）。 */
    const CUSTOM = 'user:custom';
    const customBase = { light: null, dark: null };
    const builtinBase = mode => VoxPaletteRulesData.palettes[VoxPaletteRulesData.default][mode];
    function seedInputs() {
      const source = customBase[settings.theme] || builtinBase(settings.theme);
      document.querySelectorAll('#custom-base input').forEach(input => { input.value = source[input.dataset.base]; });
    }
    function applyCustom() {
      const mode = settings.theme, base = {};
      document.querySelectorAll('#custom-base input').forEach(input => { base[input.dataset.base] = input.value.toUpperCase(); });
      customBase[mode] = base;
      const full = Object.assign({}, base, VoxPaletteRulesData.statusDefaults[mode]);
      const issues = VoxPaletteRules.validate(full, mode, VoxPaletteRulesData.statusDefaults, VoxPaletteRulesData.derivation[mode]);
      const vars = VoxPaletteRules.deriveVars(full, mode, VoxPaletteRulesData.derivation[mode]);
      let style = document.getElementById('custom-palette-style');
      if (!style) { style = document.createElement('style'); style.id = 'custom-palette-style'; document.head.appendChild(style); }
      style.textContent = ':root[data-theme="' + mode + '"][data-palette="' + CUSTOM + '"]{' +
        Object.entries(vars).map(([k, v]) => '--' + k + ':' + v).join(';') + '}';
      settings.palette = CUSTOM;
      applySettings(false);
      document.querySelectorAll('[data-set-palette]').forEach(b => b.setAttribute('aria-pressed', 'false'));
      document.getElementById('custom-report').textContent = issues.length ? '⚠ ' + issues.join('；') : '✓ 对比度校验通过（规则 §5）';
    }
    function resetCustom() {
      customBase.light = customBase.dark = null;
      const style = document.getElementById('custom-palette-style');
      if (style) style.remove();
      settings.palette = VoxPaletteRulesData.default;
      applySettings(true);
      seedInputs();
      document.getElementById('custom-report').textContent = '';
    }
    document.getElementById('apply-custom').addEventListener('click', applyCustom);
    document.getElementById('reset-custom').addEventListener('click', resetCustom);
    seedInputs();

    /* 资源 Key 面板 */
    function updateKeys() {
      const mode = settings.theme;
      const p = VoxPaletteRulesData.palettes[settings.palette] || VoxPaletteRulesData.palettes[VoxPaletteRulesData.default];
      const base = Object.assign({}, p[mode], VoxPaletteRulesData.statusDefaults[mode]);
      const vars = VoxPaletteRules.deriveVars(base, mode, VoxPaletteRulesData.derivation[mode]);
      document.getElementById('resource-key-list').innerHTML = Object.entries(vars).map(([key, value]) => {
        const color = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(value) || /^rgba?\(/.test(value);
        return '<div class="resource-key">' + (color ? '<i style="background:' + value + '"></i>' : '') + '<b>' + key + '</b><span>' + value + '</span></div>';
      }).join('');
    }
    const observer = new MutationObserver(updateKeys);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-palette'] });
    updateKeys();
  }

  /* ---------- 启动 ---------- */
  document.addEventListener('DOMContentLoaded', () => {
    injectSprite();
    applySettings(false);
    const page = document.body.dataset.page;
    if (page === 'hub') return;
    buildShell();
    applySettings(true);
    if (page === 'dashboard') renderDashboard();
    if (page === 'articles') bindArticles();
    if (page === 'detail') bindDetail();
    if (page === 'audit') renderAudit();
    if (page === 'publishing') renderPublishing();
    if (page === 'appearance') renderAppearance();
  });
})();

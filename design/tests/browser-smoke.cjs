// Vox 原型全量冒烟：视图切换、图片库、AI 助手、MCP 四分页与联动。
// 需先安装依赖：npm ci --prefix design/tests && npx --prefix design/tests playwright install chromium
// 用法：node design/tests/browser-smoke.cjs（环境变量 VOX_PREVIEW_URL 覆盖默认地址）
const { chromium } = require('playwright');

const base = process.env.VOX_PREVIEW_URL || 'http://127.0.0.1:8791/Vox.html';
let ok = 0;
function t(name, cond) { if (!cond) throw new Error('FAIL: ' + name); ok++; console.log('  ✓ ' + name); }

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });

  await page.goto(base, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.appwin');

  // 1) 六大视图切换
  for (const v of ['library', 'images', 'publish', 'site', 'mcp', 'settings']) {
    await page.click(`.nav .it[data-view="${v}"]`);
    t('视图切换 ' + v, await page.$eval('#view-' + v, el => el.classList.contains('active')));
  }

  // 2) 图片库：渲染 / 筛选 / 面板 / 孤儿清理
  t('图片卡渲染', (await page.$$('#agrid .acard')).length >= 10);
  await page.selectOption('#imgFRef', '孤儿');
  t('孤儿筛选', (await page.$$('#agrid .acard')).length === 3);
  await page.selectOption('#imgFRef', '全部引用状态');
  await page.click('#agrid .acard');
  t('选中出面板', await page.$eval('#apanel', el => el.style.display !== 'none'));
  t('面板含操作按钮', (await page.$$('#apanel .ops .btn')).length >= 4);
  page.on('dialog', d => d.accept());
  await page.click('#btnOrphanClean');
  await page.waitForTimeout(100);
  t('孤儿清理后归零', (await page.textContent('#orphanN')).trim() === '0');

  // 3) 文章库编辑按钮 → 写作页
  await page.click('.nav .it[data-view="library"]');
  await page.click('text=C#百万对象序列化深度剖析 >> xpath=ancestor::tr >> text=编辑');
  t('编辑跳写作页', await page.$eval('#view-write', el => el.classList.contains('active')));
  t('加载对应文章', (await page.textContent('#elines')).includes('C#百万对象序列化'));

  // 4) AI 助手：生成 → 采纳 → 审计 +1
  await page.click('#btnAi');
  t('AI 弹层打开', await page.$eval('#aiPop', el => !el.hidden));
  const before = await page.evaluate(() => MCP_AUDIT.length);
  await page.click('.ai-chips .chip:first-child');
  await page.waitForSelector('.ai-res', { timeout: 3000 });
  await page.click('.ai-res .ops .primary');
  t('AI 采纳计入审计', (await page.evaluate(() => MCP_AUDIT.length)) === before + 1);

  // 5) MCP：分页 / 客户端配置 / 模拟写入确认 / 审计过滤
  await page.click('.nav .it[data-view="mcp"]');
  for (const tab of ['clients', 'tools', 'audit', 'server']) {
    await page.click(`#mcpSeg button[data-t="${tab}"]`);
    t('MCP 分页 ' + tab, await page.$eval('#mcp' + tab.charAt(0).toUpperCase() + tab.slice(1), el => el.style.display !== 'none'));
  }
  await page.click('#mcpSeg button[data-t="clients"]');
  await page.click('#mcpClientSeg button[data-c="zcode"]');
  t('ZCode HTTP 配置', (await page.textContent('#mcpClientCfg')).includes('"url"'));
  await page.click('#btnDemoWrite');
  await page.click('#mcpReqBox .ops .primary');
  t('写入确认流完成', (await page.textContent('#mcpReqBox')).includes('已允许'));
  await page.click('#mcpSeg button[data-t="audit"]');
  await page.selectOption('#mcpFRes', '已拒绝');
  const denyRows = (await page.$$('#mcpAuditTbl tr')).length - 1;
  t('审计拒绝过滤', denyRows >= 1);
  await page.selectOption('#mcpFRes', '全部结果');

  // 6) 停服全局联动
  await page.click('#mcpSeg button[data-t="server"]');
  await page.click('#swMcp');
  t('状态条变已停止', (await page.textContent('#mcpStateTxt')).includes('已停止'));
  t('导航角标归零', (await page.textContent('#mcpNavCnt')).trim() === '0');
  await page.click('#swMcp');

  // 7) 发布历史
  await page.click('.nav .it[data-view="publish"]');
  await page.click('#pubSeg button[data-p="history"]');
  t('发布历史渲染', (await page.$$('#phTable tr')).length - 1 >= 5);
  await page.selectOption('#phFRes', '失败');
  t('历史失败过滤', (await page.textContent('#phTable')).includes('401'));

  // 8) 站点数据其他 json
  await page.click('.nav .it[data-view="site"]');
  await page.click('#siteNav a[data-f="friend-links.json"]');
  t('友链表渲染', (await page.textContent('#scontent')).includes('tools.dotnet9.com'));

  // 9) 文章库筛选与搜索（JS 渲染表格）
  await page.click('.nav .it[data-view="library"]');
  await page.selectOption('#libFStatus', '草稿');
  t('库筛选-草稿', (await page.$$('#libTable tr')).length === 2);
  await page.selectOption('#libFStatus', '全部状态');
  await page.fill('#libSearch', 'SQLite');
  t('库搜索-SQLite', (await page.$$('#libTable tr')).length === 2 && (await page.textContent('#libTable')).includes('SQLite'));
  await page.fill('#libSearch', '');

  // 10) 发布矩阵：状态过滤 + 平台列聚焦
  await page.click('.nav .it[data-view="publish"]');
  await page.click('#pubSeg button[data-p="matrix"]');
  await page.selectOption('#pmFStatus', '仅失败');
  t('矩阵-仅失败', (await page.$$('#pmTable tr')).length === 2);
  await page.selectOption('#pmFStatus', '全部文章');
  await page.selectOption('#pmFPlat', '公众号');
  t('矩阵-列聚焦', await page.$eval('#pmTable th:nth-child(3)', el => el.style.display !== 'none')
    && await page.$eval('#pmTable th:nth-child(4)', el => el.style.display === 'none'));
  await page.selectOption('#pmFPlat', '全部平台');

  // 11) 导图样式弹层：无页签、①结构②配色平铺、六卡六色
  await page.click('.nav .it[data-view="write"]');
  await page.click('#modeSeg button[data-m="map"]');
  await page.click('#btnMapStyle');
  t('弹层无页签', (await page.$$('#mapPop .appearance-tabs')).length === 0);
  t('步骤标签×2', (await page.$$('#mapPop .map-step-label')).length === 2);
  const schemeColors = await page.$$eval('#mapStyleGrid .map-scheme-card .type-swatch-scheme',
    els => new Set(els.map(e => getComputedStyle(e).backgroundColor)).size);
  t('配色六卡六色', schemeColors === 6);
  await page.click('#mapClose');
  await page.click('#modeSeg button[data-m="src"]');

  if (errors.length) throw new Error('页面 JS 错误:\n' + errors.join('\n'));
  console.log(`PASS: browser-smoke（${ok} 项）`);
  await browser.close();
})().catch(e => { console.error(e.message); process.exit(1); });

# design 原型目录

Vox 的界面与交互唯一样式事实源：原生 Avalonia 实现由这里的令牌、配色、排版与动作表反向同步。全部资源本地相对路径引用，零网络依赖，双击即可离线打开。

## 打开方式

```powershell
# 直接双击 Vox.html 即可（离线自治）；或起个静态服务器获得稳定地址
npx http-server design -p 8791     # http://127.0.0.1:8791/Vox.html
node design/scripts/check-prototype.cjs   # 原型门禁（零依赖）
```

## 结构

| 位置 | 内容 |
| --- | --- |
| `Vox.html` | 主原型：写作 / 文章库 / 图片库 / 发布中心 / 站点数据 / MCP 服务 / 设置七大视图 + 全部弹层与脚本 |
| `安装器-*.html` `卸载-*.html` | 三平台安装与卸载流程原型（Windows 的「启动 Vox」跳回 Vox.html） |
| `assets/tokens.css` | 设计令牌：TKCode.Arc 视觉语言 → CSS（八色强调、密度三档、动效三档、明暗） |
| `assets/app.css` | 组件样式（含 MCP 服务视图、图片库、AI 助手指令条等） |
| `assets/vendor/` | d3-hierarchy + vox-map-layout（导图布局），字节原样保留 |
| `scripts/check-prototype.cjs` | 原型门禁，零第三方依赖 |
| `tests/browser-smoke.cjs` | Playwright 全量冒烟（需先安装依赖） |

## 七大视图速览

- **写作**：源码 / 分屏 / 预览 / 导图四模式；右下角 AI 助手指令条（预设 + 自定义指令，采纳前出 diff 预览）
- **文章库**：统计 + 筛选 + 表格，「编辑」跳写作页加载对应文章
- **图片库**：缩略图墙（年份 / 引用状态 / 格式 / 文件名筛选）、选中图片看引用关系、孤儿图清理（git 可回滚口径）
- **发布中心**：网站（git 同步）/ 多平台矩阵 / 发布前检查 / 发布历史四分页
- **站点数据**：categories / friend-links / timelines / search-keywords / tools 五个 json 可切换
- **MCP 服务**：服务（端口 / 令牌 / 授权范围 / 安全）/ 客户端（五种 AI 客户端接入配置 + 模拟调用确认流）/ 工具目录（18 工具逐个上下线）/ 调用审计（筛选 + 导出），另暴露 Resources 与 Prompts、服务页有实时调用尾随
- **设置**：写作保存 / 内容仓库 / 链接策略 / git 凭据 / 平台账户 / **大模型**（服务商 + Key + AI 助手开关）/ MCP / 关于

## 一致性约定

- 内置 AI 与 MCP 服务共用同一工具层：AI 采纳写稿会计入 MCP 调用审计（客户端名「内置 AI」）。
- titles 映射的「N 篇」计数与导航 `.cnt` 徽标必须一致（门禁第 3 项检查）。
- 图片引用相对路径为唯一规范形态，CDN 域名渲染时拼接。

## 门禁检查项

1. 引用资产必须存在；禁止网络引用（离线自治）。
2. `data-view` 与 `titles` 映射互为镜像（抓死键 / 漏键）。
3. titles「N 篇」与导航徽标数字一致（防口径漂移，如 37 vs 536）。

## 浏览器冒烟（可选）

```powershell
npm ci --prefix design/tests        # 仓内锁定 playwright
npx --prefix design/tests playwright install chromium   # 首次需下载浏览器
node design/tests/browser-smoke.cjs # 默认打 http://127.0.0.1:8791/Vox.html，可用 VOX_PREVIEW_URL 覆盖
```

脚本校验：六大视图切换、图片库筛选与面板、AI 指令条生成与采纳（审计 +1）、MCP 四分页 / 模拟写入确认流 / 审计过滤、停服全局联动，并捕获页面 JS 错误。

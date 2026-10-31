<div align="center"><img src="logo.svg" width="88" alt="Vox · 维声"></div>

# Vox · 维声

一处落笔，处处有声。

Vox 是基于 .NET 与 Avalonia 的桌面**写作环境、内容仓库治理与多平台分发工作台**，第一目标是服务自建站点 [CodeWF](https://codewf.com) 的内容源仓库 [Assets.Dotnet9](https://github.com/dotnet9/Assets.Dotnet9)：写作、文章/图片管理、站点数据、Git 同步、多平台发布与状态追踪在一个应用内闭环。

作者：沙漠尽头的狼 · 出品：[码坊 CodeWF](https://codewf.com) · 姊妹产品：[Vex · 维刻](https://github.com/dotnet9/Vex)（独立维护）· 当前状态：**V1 原型与主要工作区基线已冻结，进入实现对齐与发布能力完善阶段**

> 代码不是护城河，闭环才是。

## 为什么做 Vox

"我直接用 VS Code 打开内容仓库就能改所有文件，为什么要用 Vox？"——因为 **VS Code 看到的是文件，Vox 看到的是文章、图片与分发状态**：

1. **写作工作区**：Markdown 源码、实时预览、思维导图、元数据与自动保存协同。
2. **仓库语义视图**：文章库按标题、日期、分类、专辑、标签与平台状态组织内容。
3. **图片资产管理**：导入、缩略图、引用关系、封面与孤儿图治理。
4. **发布闭环**：Git 网站发布、多平台 API/复制助手、发布检查、历史与更新线索。
5. **自动化入口**：MCP 服务让受控 AI 客户端按权限读取和维护内容。

## 当前产品基线

主原型：[design/Vox.html](design/Vox.html) · 共享样式：[design/assets/app.css](design/assets/app.css)

当前主窗口冻结为七个导航工作区：

- **写作**：文章列表 + 源码/分屏/预览/思维导图 + 元数据/大纲/图片面板。
- **文章库**：统计、筛选、搜索、平台状态与分页。
- **图片库**：图片筛选、缩略图墙、引用详情与孤儿图清理。
- **发布中心**：网站 Git 工作区、多平台矩阵、发布前检查、发布历史。
- **站点数据**：维护 `site/*.json`。
- **MCP 服务**：服务配置、客户端、工具目录、调用审计。
- **设置**：仓库、保存、链接策略、凭据、平台、LLM、外观等。

原型中的文章数、图片数、在线客户端数、发布状态等均为演示数据；正式实现必须读取真实仓库与本地状态，不得照搬固定值。

## 接下来优先完善

1. **发布可靠性**：平台适配器、远程文章 ID/URL、更新能力、发布结果核验与失败重试。
2. **发布状态闭环**：持久化任务与快照，源文章变化后准确标记“需更新”。
3. **跨平台复制助手**：平台渲染画像、HTML/富文本打包、剪贴板格式与字段复制。
4. **质量与交付**：补齐自动化测试、打包/升级验证与 Windows/macOS/Linux 实机体验。
5. **通用化**：在第二种真实仓库验证后，再抽象仓库画像接口。

## 目标平台与接入策略

接入能力分三级：**官方 API**、**实验性非官方接口**、**复制助手**。优先选择可维护、可验证的官方通道；非官方接口必须明确标注风险，并提供复制助手兜底。

| 平台 | 接入方式 | 当前定位 |
| --- | --- | --- |
| 博客园 | MetaWeblog | 已有适配，持续验证 |
| CSDN | 编辑器内部接口（Cookie + HMAC） | 实验性，非官方，可能随平台变更失效 |
| 微信公众号 | 官方草稿/发布 API | 持续完善 |
| 知乎 | 复制助手 | 无公开发布 API 时不模拟官方接口 |
| 掘金、今日头条、B 站专栏等 | 评估官方能力；否则复制助手 | 后续评估 |
| GitHub / Gitee | git 同步 Markdown 镜像 | 后续评估 |

## 技术栈

.NET 10 · Avalonia 12.1.4 · [TKCode.Arc](https://github.com/AtomUICommunity/TKCode.Arc)（AtomUI 底座，Arc 设计语言、强调色与弹簧动效）· [TKCode.StreamMark.*](https://github.com/TKCode/StreamMark)（Markdown 实时预览内核）· LibGit2Sharp（内置 git）· CommunityToolkit.Mvvm · AvaloniaEdit（Vex 同款，已在 Avalonia 12.1.4 + NativeAOT 生产验证）。

跨平台：支持 Windows / macOS / Linux，目标 **win-x64、linux-x64、linux-arm64、osx-x64、osx-arm64** 五个 RID；发布沿用 Vex 的 GitHub Actions 五平台原生 runner 矩阵。**分发只走 GitHub Releases**（原型见 [design/](design/Vox软件原型.html)）：Windows 安装器为 Arc 品牌向导，macOS 为标准 DMG 拖拽安装（签名 + 公证），Linux 由系统软件中心 / 包管理器接管——均遵循各平台惯例；应用内检查更新直接从 Releases 下载替换，不走应用商店。品牌资产（logo.svg / png / ico）在仓库根目录统一维护。

不依赖 Semi.Avalonia / Ursa.Avalonia / CodeWF.Avalonia 系组件（编辑器与 Vex 同用 AvaloniaEdit，但包依赖各自独立）。

## 文档

- [产品需求基线](docs/需求.md) —— 功能范围、用户工作流、非目标、开放问题
- [架构与技术选型](docs/架构.md) —— 解决方案结构、依赖锁版、git 分层、链接策略
- [信息架构](docs/信息架构.md) —— 页面结构与旧原型处置
- [软件操作手册](docs/软件操作手册.md) —— 原型操作说明与全量界面截图
- [决策记录](docs/决策记录.md) —— 为什么这么定（ADR-lite）
- [Assets.Dotnet9 体检基线](docs/Assets.Dotnet9体检基线.md) —— 2026-10-23 存量快照，体检规则的验收基准
- [技术验证清单](docs/技术验证Spike.md) —— 动工前必须跑通的 4 个 spike
- [设计原型](design/index.html) —— 旧版（Vex 风格），仅信息架构参考，视觉由 Arc 主题接管

## 安全与隐私

- 平台凭据（PAT、平台 token、登录 Cookie）只保存在本机系统凭据管理器，不进入仓库、不上传任何服务器
- git 操作与发布动作由本机直接发起，无中间服务
- 非官方接口仅用于发布本人原创内容，保持低频，避免触发平台风控

## 相关产品

- [Vex · 维刻](https://github.com/dotnet9/Vex)：跨平台 Markdown 编辑器，通用离线文档编辑，与 Vox 独立维护
- [CodeWF](https://codewf.com)：前后端分离的 Markdown 博客站点
- [Assets.Dotnet9](https://github.com/dotnet9/Assets.Dotnet9)：站点内容源仓库，Vox 的首要服务对象

## 许可证

[MIT](LICENSE) © 沙漠尽头的狼(codewf.com)

<div align="center"><img src="logo.svg" width="88" alt="Vox · 维声"></div>

# Vox · 维声

一处落笔，处处有声。

Vox 是基于 .NET 与 Avalonia 的桌面**写作环境与内容仓库治理台**，第一目标是服务自建站点 [CodeWF](https://codewf.com) 的内容源仓库 [Assets.Dotnet9](https://github.com/dotnet9/Assets.Dotnet9)：写作、配图、元数据、体检、同步，一个应用闭环；远期扩展为通用文章管理器，"一文多发"能力（复制助手、API 发布）作为第二、第三里程碑推进。

作者：沙漠尽头的狼 · 出品：[码坊 CodeWF](https://codewf.com) · 姊妹产品：[Vex · 维刻](https://github.com/dotnet9/Vex)（独立维护）· 当前状态：**M1 设计冻结，筹备动工**

> 代码不是护城河，闭环才是。

## 为什么做 Vox

"我直接用 VS Code 打开内容仓库就能改所有文件，为什么要用 Vox？"——因为 **VS Code 看到的是文件，Vox 看到的是文章和图片**：

1. **图片入库管线**：网上找的图、AI 生成的图，拖入 / 粘贴即按文章 slug 命名、落到正确的年月目录、插入引用、可一键设为封面。编辑期使用相对路径，发布时改写为 CDN 域名（`img1.dotnet9.com`）。
2. **仓库语义视图**：列表展示标题 / 日期 / 分类 / 专辑 / 标签 / 草稿状态，而不是 `2026/09/xxx.md` 文件树；筛"2026 年没封面的草稿"是一个过滤器的事。
3. **体检与修复**：缺专辑、缺 author、图片死链、孤儿图、扩展名配对错乱……列出、定位、批量修复，历史债务一次清干净。
4. **发布闭环**（M2/M3）：多平台复制助手与 API 直发，这是通用编辑器永远不会有的那一环。

## 里程碑

### M1 · 写作环境与仓库治理（当前）

- **仓库索引**：扫描 `YYYY/MM/slug.md` + `slug.yml` + 图片 + `site/*.json`，建立中立领域模型
- **新建文章向导**：标题 → slug / 日期自动生成 → 简述 → 大纲骨架，产出 md + yml 脚手架，跳入编辑器慢慢细化
- **写作环境**：Markdown 源码编辑 + StreamMark 实时预览（相对路径图片解析）+ 大纲跳转 + 元数据侧栏表单（点封面缩略图换图，而不是编辑 yml）
- **图片库**：缩略图网格、按年月 / 所属文章分组、孤儿图筛选、拖入即入库、复制 CDN 链接 / 相对路径
- **体检与修复**：缺专辑、缺 author、图片死链、孤儿图、扩展名配对修复、过期 / 低质线索——系统给线索，人工拍板
- **内置 git**：不要求本机安装 git；HTTPS + PAT 推送（凭据存系统凭据管理器），SSH 推送自动降级外部 git；删除永远走 git，可回滚
- **站点数据表单**（`site/*.json`，仅 zh-CN）与**设置**

### M2 · 通用复制助手（规划）

- 平台画像（platform profile）声明式配置，渲染管线对平台差异零感知
- Markdown → 平台 HTML 渲染管线，支持三类模式：原生 Markdown 直贴 / HTML + CSS / HTML + 内联样式
- 微信公众号内联样式引擎：元素级 style 映射 + 服务端代码高亮
- 剪贴板打包：CF_HTML 富文本 + 纯文本兜底，标题 / 话题 / 封面 / 摘要 / 正文字段分组复制
- 置顶浮动面板：点字段即复制、快捷键直达，配合平台创作页逐框粘贴
- 发布清单：每篇文章 × 每个平台的发布状态一目了然

### M3 · API 自动发布（规划）

- 本地发布状态库（SQLite）：平台、文章 ID、链接、时间、内容快照哈希
- 源文件变更检测：发布后改过原文的平台自动标"需更新"，支持一键同步
- 首批直发通道：博客园（MetaWeblog）、CSDN（OpenAPI）、微信公众号（官方草稿/发布 API）
- 非官方接口通道：掘金、今日头条、B 站专栏等，cookie 失效自动降级为复制助手

## 目标平台（M2/M3 远期规划）

接入方式分三级：**API 直发**（官方接口）、**非官方接口**（社区逆向，谨慎低频）、**复制助手**（渲染打包 + 粘贴，所有平台的基础兜底）。

| 平台 | 接入方式 | 状态 |
| --- | --- | --- |
| 博客园 | API 直发（MetaWeblog） | 规划中 |
| CSDN | API 直发（OpenAPI） | 规划中 |
| 微信公众号 | API 直发（官方草稿/发布 API） | 规划中 |
| 掘金 | 非官方接口，降级复制助手 | 规划中 |
| 今日头条 | 非官方接口，降级复制助手 | 规划中 |
| B 站专栏 | 非官方接口，降级复制助手 | 规划中 |
| InfoQ 写作社区 | 非官方接口，降级复制助手 | 规划中 |
| 腾讯云开发者社区 | 非官方接口，降级复制助手 | 规划中 |
| 阿里云开发者社区 | 非官方接口，降级复制助手 | 规划中 |
| 知乎 | 复制助手（无公开 API） | 规划中 |
| 简书 | 复制助手 | 规划中 |
| 51CTO 博客 | 复制助手 | 规划中 |
| 华为云开发者社区 | 复制助手 | 规划中 |
| Boss 有乐 | 复制助手 | 规划中 |
| GitHub / Gitee | git 同步 Markdown 镜像 | 规划中 |

## 技术栈

.NET 10 · Avalonia 12.1.4 · [TKCode.Arc](https://github.com/AtomUICommunity/TKCode.Arc)（AtomUI 底座，Arc 设计语言、强调色与弹簧动效）· [TKCode.StreamMark.*](https://github.com/TKCode/StreamMark)（Markdown 实时预览内核）· LibGit2Sharp（内置 git）· CommunityToolkit.Mvvm · AvaloniaEdit（Vex 同款，已在 Avalonia 12.1.4 + NativeAOT 生产验证）。

跨平台：支持 Windows / macOS / Linux，目标 **win-x64、linux-x64、linux-arm64、osx-x64、osx-arm64** 五个 RID；发布沿用 Vex 的 GitHub Actions 五平台原生 runner 矩阵。**分发只走 GitHub Releases**（原型见 [design/](design/Vox软件原型.html)）：Windows 安装器为 Arc 品牌向导，macOS 为标准 DMG 拖拽安装（签名 + 公证），Linux 由系统软件中心 / 包管理器接管——均遵循各平台惯例；应用内检查更新直接从 Releases 下载替换，不走应用商店。品牌资产（logo.svg / png / ico）在仓库根目录统一维护。

不依赖 Semi.Avalonia / Ursa.Avalonia / CodeWF.Avalonia 系组件（编辑器与 Vex 同用 AvaloniaEdit，但包依赖各自独立）。

## 文档

- [M1 需求](docs/需求.md) —— 功能范围、用户工作流、非目标、开放问题
- [架构与技术选型](docs/架构.md) —— 解决方案结构、依赖锁版、git 分层、链接策略
- [信息架构](docs/信息架构.md) —— 页面结构与旧原型处置
- [软件操作手册](docs/软件操作手册.md) —— 原型操作说明与全量界面截图
- [决策记录](docs/决策记录.md) —— 为什么这么定（ADR-lite）
- [Assets.Dotnet9 体检基线](docs/Assets.Dotnet9体检基线.md) —— 2026-10-23 存量快照，体检规则的验收基准
- [技术验证清单](docs/技术验证Spike.md) —— 动工前必须跑通的 4 个 spike
- [设计原型](design/index.html) —— 旧版（Vex 风格），仅信息架构参考，视觉由 Arc 主题接管

## 安全与隐私

- 平台凭据（PAT、平台 token、cookie）只保存在本机系统凭据管理器，不进入仓库、不上传任何服务器
- git 操作与发布动作由本机直接发起，无中间服务
- 非官方接口仅用于发布本人原创内容，保持低频，避免触发平台风控

## 相关产品

- [Vex · 维刻](https://github.com/dotnet9/Vex)：跨平台 Markdown 编辑器，通用离线文档编辑，与 Vox 独立维护
- [CodeWF](https://codewf.com)：前后端分离的 Markdown 博客站点
- [Assets.Dotnet9](https://github.com/dotnet9/Assets.Dotnet9)：站点内容源仓库，Vox M1 的服务对象

## 许可证

[MIT](LICENSE) © 沙漠尽头的狼(codewf.com)

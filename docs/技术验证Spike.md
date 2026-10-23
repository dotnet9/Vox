# 技术验证清单（Spikes）

动工前必须跑通的 4 个半天级验证。**Spike** = 为消除某个具体技术不确定性而写的一次性试验程序：目的不是产出产品代码，而是回答"这条路走不走得通"，验证完即可丢弃。全部通过后再搭正式骨架，避免骨架搭完推倒重来。

| # | 验证点 | 方法 | 通过标准 | 状态 |
| --- | --- | --- | --- | --- |
| 1 | **StreamMark 相对路径图片解析**（写作环境可行性命门） | 建 demo 工程，MarkdownView 加载含相对路径图片的 md，寻找/实现宿主图片解析 API | 预览中本地图片正常显示；CDN URL 可映射到本地文件 | **实现中收敛**：库 API 确认（`MarkdownServices.ImageBaseUri` + `ImagePolicy.AllowedFileRoots`，CDN URL 由宿主解析回调反查本地文件），已接入写作页；待对真实仓库人工验收 |
| 2 | **TextMate 插件版本对齐** | 引入 AvaloniaEdit 12.0.0 + AvaloniaEdit.TextMate，与 Arc/StreamMark 依赖链的 TextMateSharp 版本对齐 | 编辑/高亮/滚动正常，无包版本冲突 | **已通过**：TextMateSharp 统一 2.0.4（`Directory.Packages.props` 锁定），编辑器已装 TextMate markdown 高亮 |
| 3 | **LibGit2Sharp HTTPS + PAT 推送** | 对 Assets.Dotnet9 的真实远端做一次提交推送（测试分支） | 推送成功；凭据经 `ICredentialStore` 读取（Windows 凭据管理器已实现，macOS/Linux 暂用加密文件兜底） | 未开始（同步页一键提交并推送已就绪，待真远端验证） |
| 4 | **Arc 主题 × StreamMark 视觉融合** | 同一窗口：Arc 控件 + StreamMark（Theme.AtomUI），切换明暗与强调色 | 视觉协调，无需大量自定义样式 | **机制已落地**：`StreamMarkTheme` → `AtomUIMarkdownTheme` 顺序注册，Md.* 映射 AtomUI token 跟随 Arc 明暗/强调色；待人工视觉验收 |

## 记录

（每个 spike 完成后在此追加：结论、遇到的问题、对架构的影响、代码片段存放位置）

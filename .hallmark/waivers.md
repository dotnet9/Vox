# Hallmark 豁免清单 — design/（Avalonia 桌面客户端设计蓝本）

> 审计背景：design/ 下是桌面客户端的 HTML 设计蓝本（主界面 + 安装器 + 卸载器），非营销页。
> 自绘 titlebar、桌面窗口装饰、原型内联色值属有意为之。以下规则×语境组合裁定为噪音，不计入待修。

| 规则 | 规模 | 豁免理由 |
|---|---|---|
| 48（颜色字面量出 token） | ~270 | 拟真语义色（macOS 红黄绿灯 #febc2e/#28c840/#ff5f57、VS 关闭钮 #e81123、GitHub diff 红绿、语法高亮 .kw/.st/.lnk）与阴影微调 rgba(0,0,0,.04)——实现侧（TKCode.Arc）有对应主题资源，原型字面量是保真手段 |
| 7a（纯 #fff 前景） | ~13 | 语义色底/强调底上的白字（关闭钮 hover 红底、封面遮罩、任务勾选）——实现侧为 on-accent/on-status 令牌 |
| 63（中点元信息串） | ~18 | 桌面应用元信息惯例（Finder/VS Code 同款）：状态栏「当前 x · 通道 y」、排版卡规格「简约 · 无衬线 · 1.9 行距」（功能性规格标注） |
| 67（大写小标签） | ~9 | 「CONTENT WORKSPACE / CONTENT LIBRARY / PUBLISH WORKFLOW」是设计系统英文副题（kicker 体系）；其余为中文误判大写 |
| 24b（非 4px 间距） | ~275 | TKCode.Arc 密度体系 rem 换算（0.9231rem×14≈13px 等），与 Avalonia 实现像素口径锁定 |
| 47a（手绘窗口 chrome） | 3 | 安装器/卸载器/主界面的自绘标题栏是**演示对象本身**，非网页模拟浏览器 |
| 19（破折号 —） | 6 | 中文提示框惯例（「已安装 · …」），hallmark 规则针对英文 em-dash 滥用 |
| 2a（body.stage 氛围渐变） | 6 | 安装器/卸载器舞台背景属 TKCode.Arc 视觉语言；`.g4` 卡片渐变已改单锚色（真问题已修） |
| 3（引用侧条纹） | 6 | Markdown blockquote 左竖线是渲染语义，非装饰 |
| 20（无 Hallmark stamp） | 7 | 原型有自己的门禁体系（design/scripts/check-prototype.cjs，112 项）；stamp 流程不适用 |

**已修真问题记录**（本轮 hallmark 修复，见 git log）：
- 10a transition:all/width-left 动画（Vox 17 + 卸载-Windows 3 + 安装器-Linux 2）
- 34 overflow-x hidden/100vw（8）
- 33 svg 无 aria（31）
- 56 双 sticky 叠加（1）
- 2a .g4 紫粉渐变（1）
- 30a 按钮 emoji（Vox 1）
- 50 表头溢出（1）+ 54 eyebrow（1）+ F6 tabular-nums（8）+ F23 meta description（1）+ 27 降动效硬化（tokens.css）

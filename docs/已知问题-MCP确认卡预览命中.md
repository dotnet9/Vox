# 已知问题（已降级）：MCP 写确认卡（带 preview）点击失效——未能稳定复现

> 2026-11-15 凌晨实机排查记录。**结论修正（04:10 复测）**：带 preview 的确认卡点击失效
> **未能稳定复现**——同一天内 2 次失败、2 次成功；失败案例均出现在确认卡长时间无人处理的
> 状态下。已上线的 5 分钟超时兜底（8f85688）把最坏情况控制在「自动拒绝并撤卡」，不再
> 永久挂起。image_import 已**正式开放**（实现全链路验证：确认卡→下载→落盘→审计 okc）。
> 若再次遇到点击失效，按下方调试入口排查。

## 现象（历史记录，未稳定复现）

`McpConfirmModal` 带 preview（`request.Preview` 非空，弹层多一个 URL/文本预览框）时：

- 视觉上确认卡正常显示（截图可见「客户端请求写入 image_import」卡片、允许/拒绝按钮齐全）；
- 鼠标点击「允许本次写入」按钮（多坐标：client (943,527) / (943,551) / (943,520)）全部无效；
- `Esc` / `Tab` / `Enter` 键盘全部无效；
- UIA `FindAll` 在主窗口树中**找不到**「允许/拒绝」按钮（ControlType Button 共 48 个，无一匹配）；
- `AutomationElement.FromPoint` 探测按钮屏幕坐标返回**空元素 (0x0)**，探测卡区域其它点返回 `MainWindow`
  （即命中穿透到主窗口，仿佛卡不在命中层）；
- 点击卡外区域 (500,400) 却能正常触发遮罩关闭（`OnModalMaskReleased` → `CloseModal` → 卡
  `OnDetachedFromVisualTree` → `Settle(false)`）——此时审计应落 `deny`，但**审计文件无新记录**；
- 同一进程内后续交互（AI 面板其它按钮）表现正常，UI 渲染持续进行（截图画面在更新）。

## 已排除

- **坐标换算**：窗口 place 到 (60,40)，`ClientToScreen` origin=(68,40)，自绘标题栏含在客户区内，
  图像 y 与客户区 y 零差（水平差 8px 边框已计入）；同一坐标换算在无 preview 卡（git_commit 卡）上点击成功。
- **ModalMask 背景**：`VoxMaskBg = #66000000`（非透明、参与命中）；`Modal.Skin` 背景走 `Res()` 本地动态资源，
  其余弹层（ConfirmDialog/新建向导等）点击全部正常。
- **ZIndex**：ModalMask=50 > AI 面板 host=46，视觉与截图均确认卡在面板之上。
- **ConfirmDialog 污染假设**：无 ConfirmDialog 参与的干净流程（空会话静默清空后）触发同一张卡，点击
  **成功**（审计出现 `image_import | error`，HTTP 522 为 picsum 源站超时，属网络问题非代码问题）；
  但 02:33 曾有 ConfirmDialog 确认、02:40 的无 preview 卡点击也成功——污染假设与时间线矛盾，未定论。
- **McpConfirmModal 实现**：允许钮是标准 Avalonia `Button`（`Classes btn primary`）+ Click handler
  （`Settle(true)` 先于 `CloseModal`），与无 preview 卡完全同构。

## 关键疑点（建议调试入口）

1. **preview 分支的差异只剩布局**：`previewBorder` 内嵌 `ScrollViewer { MaxHeight = 190 }`。
   怀疑该 ScrollViewer（或其对 PointerPressed 的处理）改变了 modal 的命中结构——建议在
   `McpConfirmModal` 构造 preview 处打断点，实机比对有无 preview 两种卡的可视树与 hit-test。
2. **审计缺失矛盾**：`CallToolAsync` 尾部无条件 `Audit(...)`——卡被遮罩关闭（Settle(false)→Deny）
   后审计仍无记录，说明该次 `CallTool` **从未返回**（completion 从未结算）。但 Settle(false)
   理论上必被 OnDetachedFromVisualTree 触发。建议在 `McpHostBridge.ConfirmWriteAsync` 的
   `completion.TrySetResult` 与 `McpConfirmModal.Settle` 各加一行调试日志跑一次复现。
3. 已加 **5 分钟超时兜底**（`ConfirmWriteAsync` → `WaitAsync(5min)`，8f85688）：超时自动拒绝并撤卡，
   卡不会再永久挂起 Agent 循环——即便命中问题未修，功能也能自愈。

## 复测记录（2026-11-15 04:04-04:07）

- 重建开放 image_import 后，同会话连续两次带 preview 卡点击「允许本次写入」**均成功触发工具**：
  - 第一次：raw.githubusercontent.com SSL 失败（网络环境不可达，非代码问题）——审计 error 如实透传；
  - 第二次：本地 http.server 图源——审计 `okc`，145KB 图片真实落盘（`2026/11/…png`，验证后清理）。
- 此前两次失败（02:57 卡 A）与成功的两次（exp-01 / 复测）之间唯一稳定差异是**卡停留时长**：
  失败案例卡均已挂起数分钟。与 5 分钟超时兜底的时间窗吻合，怀疑命中异常与长时间挂起相关，
  但未获直接证据；如再遇，用上方调试入口取证。

## 复现路径

内置 AI 会话（`AiAgentLoop`）+ `AllowedTools` 临时加回 `image_import` → 发「把这张网图入库：
<http(s) URL>，文件名 xxx.png」→ 确认卡弹出后点击「允许本次写入」。

## 附：凌晨实机闭环的功能（同一会话验证，均可用）

- 清空会话确认（ConfirmDialog，四语）+ ConfirmDialog 确认钮失效修复（先结算后关闭）
- 会话历史持久化：落盘/重启恢复/续聊上下文/清空删档/导出含恢复历史
- article_meta 写入意图：确认卡→允许→磁盘写入→watcher 自动同步元数据面板（外部变更检测与自动保存解耦）
- git_commit / git_push 意图：确认卡→精确路径提交→SSH remote 走系统 git 推送（ahead 0 验证）
- 防幻觉铁律：工具未调时不得断言成功（实测模型从「编造入库成功」转为如实说明无工具）
- 退出守卫：未保存修改关窗先确认（取消保留/丢弃退出两路径实机验证）

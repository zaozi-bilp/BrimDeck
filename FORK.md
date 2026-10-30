# BrimDeck · 个人魔改版

> **非官方 fork。** 本项目基于 [@MoFeng2223](https://github.com/MoFeng2223) 的
> [BrimDeck](https://github.com/MoFeng2223/BrimDeck) **v1.2.3**：
> [源码 tag](https://github.com/MoFeng2223/BrimDeck/tree/v1.2.3) ·
> [Release](https://github.com/MoFeng2223/BrimDeck/releases/tag/v1.2.3)。
>
> 我只是按自己的使用习惯改了一点功能，和原作者没有关系；原项目的版权、License 和致谢归原作者所有。
> 这个分支只在自己的 Windows 11 环境里验证过，不同播放器版本、系统版本可能有差异，请按需取用。

## 这个版本是什么

BrimDeck 是 Windows 11 上的“灵动岛”式悬浮面板：平时收起在屏幕顶部，显示音乐、额度和系统读数，鼠标移上去展开。
这个分支保留原项目的额度、媒体、系统监控和设置体系，主要围绕 **酷狗音乐**、**双岛布局** 和 **日常使用细节** 做个人化修改。

## 本分支功能

### 1. 酷狗音乐适配

- 识别酷狗音乐（AUMID `kugou`），在来源菜单和媒体页显示“酷狗音乐”。
- 酷狗通过 Windows 媒体控件只提供歌名、歌手和封面，不提供播放时间轴，也不提供可用的歌词搜索结果。
  本分支做了三件事：
  - **歌词**：按“标题 + 歌手”搜索 QQ 音乐公开目录，匹配同名录音后读取 LRC；匹配时会排除现场版、伴奏、Remix、DJ、翻唱、纯音乐等，并按时长排序；QQ 目录没有结果时再回退到 LRCLIB / 网易云。
  - **进度**：通过 UI Automation 读取酷狗自己的进度滑块（只读，不控制播放器），把 1% 的步进放在该步中间，再用墙钟推进；读不到滑块时，用首次播放上报的时间估算。
  - **时长**：匹配到的歌词会带回该录音的时长，用于限制估算位置和收起态的进度。
- 新增“歌词偏移”设置（-3000 ~ +3000 ms），用于酷狗进度较粗或个别歌曲不同步时微调。
- 歌词缓存上限从 80 首降到 48 首，减少长时间播放后的常驻内存。

> 已知限制：不同酷狗版本的 UI Automation 结构可能不同；读不到进度时会退回估算，拖动进度条后可能需要一两秒才重新对齐。

### 2. 双岛布局（一大一小）

- 可在设置中把收起态拆成 **两个岛**：左侧小岛、右侧大岛；也可以随时合并回原来的单岛。
- 每个岛可以独立选择：
  - 显示哪些收起部件（配额环、音乐、系统读数、律动条、时钟）；
  - 展开后显示哪些页面（用量、音乐、系统监控）；
  - 自己的宽度、高度、顶部位置和水平偏移。
- 两个岛独立展开：悬停/点击小岛只打开小岛的页面，大岛保持自己的状态；分开后互不挤占。
- 点击固定：收起态点击岛会打开并固定面板，再点一次关闭；不点击时仍然是原来的悬停展开、移开收起。
- 分开/合并即时生效：切换后第二个岛立刻出现或消失，第二个岛不再播放约两秒的启动圆点动画，避免“切换后只看到一个岛”的错觉；线条形态（全屏/最大化规则）下第二个岛自动隐藏，避免残影或错位。
- 第二个岛的展开页只在第一次打开时创建，平时收起不常驻第二套用量/监控页，降低内存。

### 3. 歌词显示

- 当前歌词保持主题主色，上下相邻歌词使用同一颜色的较低透明度；不再有逐字颜色渐变，也不再显示 `♪` 占位符，间奏/前奏就是空行。
- 长歌词滚动一次后停在末尾，不再回到开头；换到下一行时才从头开始滚动。
- 新增“歌词进度线”：一行单色细线，只改变长度，不改变颜色；可在设置中关闭。
- 保留“歌词偏移”，酷狗和普通播放器都可使用。

### 4. 微信消息提醒

- 监听微信（微信 4 / Weixin.exe）的新消息通知窗口：窗口显示事件 + 低频轮询，只在收起岛短暂显示“微信 · 新消息”。
- 只读取通知窗口的类名、标题和位置，不读取消息内容，不联网。
- 可在设置中关闭。

### 5. 本地活动桥（activity.json）

其他程序可以把下面这样的 JSON 写入 BrimDeck 数据目录的 `activity.json`：

```json
{ "title": "构建中", "detail": "3/5", "progress": 0.6, "seconds": 8 }
```

收起岛会短暂显示这条活动。使用 `FileSystemWatcher` + 轮询读取，只读本机文件，不启动进程、不联网。可在设置中关闭。

### 6. 视觉与稳定性

- **封面氛围光**：播放音乐时，收起岛阴影取封面主色形成柔和光晕；暂停/停止恢复普通阴影。
- **最大化/全屏切换修复**：窗口规则把面板变成线条形态时，双岛只保留第一个岛，面板位置也不再按双岛计算，避免切换全屏/最大化后收起态错位。
- **内存回收改进**：空闲回收从 DispatcherTimer 改为线程池等待，避免律动条/歌词滚动饿死回收定时器；增加 192 MB 阈值、30 秒静置和 3 分钟间隔，GC 与工作集回收放在后台线程；设置窗口关闭、第二个岛退休时也会请求回收。

## 相比原版 v1.2.3 的创新/差异

| 方面 | 原版 v1.2.3 | 本分支 |
| --- | --- | --- |
| 酷狗音乐 | 能显示系统媒体信息，但没有时间轴和歌词 | 歌词目录匹配 + 酷狗进度条读取 + 估算兜底 + 偏移设置 |
| 收起布局 | 单个岛 | 一大一小双岛，可分开/合并，内容和页面每岛自选 |
| 双岛交互 | 无 | 独立展开、点击固定、切换即时显示第二岛、线条形态自动隐藏第二岛 |
| 歌词 | 基础歌词显示 | 歌词偏移、单色相邻行、单色进度线、无 `♪` 占位 |
| 通知 | 无微信/本地活动 | 微信弹窗提醒、`activity.json` 本地活动桥 |
| 视觉 | 普通阴影 | 封面取色氛围光 |
| 内存 | 空闲 trim 使用 DispatcherTimer | 线程池等待 + 阈值/降频 + 第二岛懒加载 |
| 全屏/最大化 | 单岛切换 | 双岛在线条形态下正确隐藏，避免切屏后的收起错位 |

## 按个人偏好未包含

这些是原项目或早期改动里出现过的想法，本分支按自己的使用习惯去掉了：

- 微信/QQ 未读汇总角标；
- 桌面悬浮歌词浮层；
- 歌词逐字颜色渐变、`♪` 音符占位和 `已唱/未唱` 两套自定义颜色（当前歌词统一为主色，相邻行同色降低透明度）。

## 构建

环境：Windows 11、.NET 10 SDK（与上游一致）。

```powershell
dotnet build src/BrimDeck/BrimDeck.csproj -c Release
dotnet run --project tests/BrimDeck.Tests -c Release
```

单元测试：**2224 项通过**（`tests/BrimDeck.Tests`，纯控制台断言）。

## 版权与致谢

- 原作者：[@MoFeng2223](https://github.com/MoFeng2223)
- 原项目：<https://github.com/MoFeng2223/BrimDeck>
- 基于版本：**v1.2.3**
  - 源码：<https://github.com/MoFeng2223/BrimDeck/tree/v1.2.3>
  - Release：<https://github.com/MoFeng2223/BrimDeck/releases/tag/v1.2.3>
- License：Apache-2.0，见 [LICENSE](LICENSE)；原项目版权与致谢保持不变。

## About this fork (English)

Unofficial personal fork of [BrimDeck](https://github.com/MoFeng2223/BrimDeck) **v1.2.3** by
[@MoFeng2223](https://github.com/MoFeng2223). It adds Kugou Music lyrics/position support, a two-island
collapsed layout, WeChat popup notices, a local `activity.json` bridge, cover-colored glow and more robust
idle memory trimming. It is not affiliated with the original author; all original rights and the Apache-2.0
license remain with the upstream project.

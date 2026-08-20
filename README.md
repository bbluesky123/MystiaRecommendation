# MystiaRecommendation — 东方夜雀食堂稀客推荐插件

一个基于 **BepInEx 6 IL2CPP** 的《东方夜雀食堂》插件。稀客点单后，插件会读取当前料理、酒水、厨具、食材库存、羁绊与流行趋势，计算并显示两套可用套餐（料理 + 酒水 + 可选附加食材 + 厨具）。

## 快速安装

1. 确保游戏已安装 **BepInEx 6 IL2CPP**（[下载](https://builds.bepis.io/projects/bepinex_be)）。
2. 从 [Releases](https://github.com/bbluesky123/MystiaRecommendation/releases) 下载最新的 `MystiaRecommendation-v*.zip`，无需自行编译源码。
3. 在 `<游戏目录>\BepInEx\plugins\` 下创建 `MystiaRecommendation` 文件夹，把 ZIP 的内容解压到该文件夹。
4. 确认最终目录如下，然后启动游戏：

```text
BepInEx/
└── plugins/
    └── MystiaRecommendation/
        ├── MystiaRecommendation.dll
        └── Data/
            ├── customers_rare.json
            ├── recipes.json
            ├── beverages.json
            ├── ingredients.json
            └── area_unlock_schedule.json
```

进入营业后，插件会在稀客出现和完成点单时自动工作。如果推荐状态未及时更新，可按 F5 手动刷新。

## 热键

| 热键 | 功能 |
|------|------|
| F2 | 开关推荐卡片叠加显示 |
| F5 | 清除当晚缓存并重新计算当前活跃稀客的推荐 |

当前源码没有实现 F1 游戏内配置面板。配置由 BepInEx 自动生成的配置文件管理。

## 当前功能

- **自动识别稀客和多轮点单**：监听稀客姓名、订单生成、食物需求和酒水需求；同一轮重复回调不会重复计算。
- **多稀客支持**：按座位独立维护推荐卡片；客人付款、离座、座位换客或场景切换时自动清理。
- **运行时状态检测**：读取已解锁料理、已拥有酒水、厨具、食材数量、玩家等级、角色羁绊和流行趋势。
- **智能解锁判断**：分别处理初始、羁绊、玩家等级、任务、商店和特殊来源的料理。
- **区域开放过滤**：根据游戏内日期和 `area_unlock_schedule.json` 排除尚未开放区域相关的角色或料理。
- **食材补标签**：当料理无法直接满足食物需求时，可使用库存不少于 10 个且带目标标签的食材扩展候选。
- **夜雀厨具兜底**：正常标签匹配不可行时，如果持有夜雀厨具，会生成放宽订单标签限制的兜底方案。
- **流行趋势**：可把当前流行喜爱/厌恶作为套餐标签参与不同稀客的评分。
- **两种价格方案**：从达标候选中显示一套较高账面价格方案和一套较低价格方案，而不是简单取评分最高的前两名。
- **可交互卡片**：最多显示 8 位活跃稀客，每张卡片可拖拽，顾客概览和两套方案可分别折叠。
- **未知稀客兜底**：数据表未收录的姓名仍可按本轮食物和酒水订单标签生成基础方案。

## 推荐流程

```text
稀客出现
  → 显示“等待订单”卡片
  → 收集食物标签、酒水标签和订单预算
  → 查询料理/酒水/厨具/食材/羁绊/流行趋势
  → 生成正常匹配、食材扩展或夜雀兜底候选
  → 按 4 → 3 → 2 → 1 的最低评分依次降级
  → 必要时添加附加食材提高评分或补齐订单标签
  → 显示高价和低价两套方案
```

套餐评分为：

```text
命中的顾客正面标签数 - 命中的顾客负面标签数
```

顾客的酒水偏好标签也会加入正面标签集合。标签冲突时按游戏机制处理“大份覆盖小巧”“灼热覆盖凉爽”“肉覆盖素”“重油覆盖清淡”“饱腹覆盖下酒”。

附加食材价格单独记录；界面中的账面总价和订单预算判断使用料理价格 + 酒水价格。

## 配置

首次运行后，BepInEx 会生成插件配置文件。当前源码实际使用的配置项如下：

| 配置项 | 默认值 | 作用 |
|-------|--------|------|
| 启用叠加显示 | `true` | 推荐卡片总开关 |
| 透明度 | `0.85` | 卡片背景透明度 |
| 字体大小 | `14` | 卡片字体大小 |
| 考虑流行趋势 | `true` | 是否读取流行喜爱/厌恶并参与评分 |
| 最大额外食材数 | `3` | 补标签或提高评分时最多添加的食材数；总食材仍不超过 5 个 |
| 切换显示 | `F2` | 叠加显示热键 |
| 刷新 | `F5` | 清除缓存并刷新推荐的热键 |

源码还定义了显示位置、自动隐藏延迟、优先夜雀厨具、显示标签、显示符卡和显示羁绊等配置项，但当前版本尚未把它们完整接入推荐或 UI；修改这些配置暂时不会产生预期效果。

如果区域开放日程不准确，可直接编辑 `Data/area_unlock_schedule.json` 中的 `absoluteDay`，无需重新编译。

## 料理解锁检测

| 解锁类型 | 当前检测方法 | 说明 |
|---------|-------------|------|
| Self | 直接判定 | 初始自带料理始终可用 |
| Bond | `RunTimeAlbum.GetCharacterKizuna(Int32, out ..., out ...)` | 检查角色区域、羁绊等级以及经验已满但尚未升级的边界状态 |
| LevelUp | `RunTimePlayerData.Level` 或 `RunTimeStorage.HaveRecipe(Int32)` | 无区域限制时比较玩家等级；带区域限制时先检查区域，再查询游戏存档 |
| QuestOrEvent | `RunTimeStorage.HaveRecipe(Int32)` | 先过滤尚未开放的任务区域 |
| Shop / Special / Unknown | `RunTimeStorage.HaveRecipe(Int32)` | 直接以游戏存档为准 |

默认区域日程包括妖怪兽道（day 1）、人间之里（day 17）、博丽神社（day 34）、红魔馆（day 48）和迷途竹林（day 69）。实际完整列表以 `Data/area_unlock_schedule.json` 为准。

料理解锁、羁绊、玩家等级、日期和厨具会缓存。场景切换会自动清除缓存；当晚获得新料理或厨具后，可按 F5 立即重新读取。

## 从源码构建

前置条件：

- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- Python 3
- 已正确安装 BepInEx 6 IL2CPP 的游戏目录

```powershell
git clone https://github.com/bbluesky123/MystiaRecommendation.git
cd MystiaRecommendation
python build.py "D:\你的游戏路径"
```

也可以直接修改 `MystiaRecommendation.csproj` 中的 `<GameDir>`，再运行：

```powershell
python build.py
```

脚本依次执行 `dotnet restore` 和 Release 构建。构建完成后，MSBuild 会把 DLL 和 `Data/*.json` 复制到：

```text
<游戏目录>\BepInEx\plugins\MystiaRecommendation\
```

## 项目结构

```text
MystiaRecommendation/
├── Plugin.cs                         # 插件入口、推荐编排、运行时状态与解锁检测
├── MyPluginInfo.cs                   # 插件 GUID、名称和版本
├── MystiaRecommendation.csproj      # .NET/BepInEx/游戏程序集引用与部署目标
├── build.py                          # Restore + Release 构建脚本
├── Patches/
│   ├── CustomerPatch.cs              # 当前实际注册的稀客、订单和离场 Harmony Hook
│   ├── InventoryPatch.cs             # 预留文件，当前没有补丁实现
│   └── RecipePatch.cs                # 预留文件，当前没有补丁实现
├── Engine/
│   ├── CustomerDataEngine.cs         # 稀客数据加载与查询
│   ├── RecipeMatcher.cs              # 料理/酒水候选、补食材、评分和结果选择
│   ├── GameStateCache.cs             # 早期缓存模型，当前主流程未使用
│   └── SimpleJson.cs                 # 项目内置轻量 JSON 解析器
├── UI/
│   ├── GUIBehaviour.cs               # 常驻 Unity 组件、热键、场景和健康检查
│   └── OverlayRenderer.cs            # 推荐卡片布局、拖拽、折叠和绘制
├── Config/
│   └── PluginConfig.cs               # BepInEx 配置项定义
├── Data/
│   ├── customers_rare.json           # 69 位稀客、偏好、预算、地点、符卡和台词映射
│   ├── recipes.json                  # 190 个料理及解锁条件
│   ├── beverages.json                # 48 种酒水
│   ├── ingredients.json              # 70 种食材及标签
│   └── area_unlock_schedule.json     # 区域开放日程
```

`GameStateCache.cs`、`InventoryPatch.cs` 和 `RecipePatch.cs` 是早期设计或预留结构；当前运行时状态由 `Plugin.cs` 在生成推荐时主动查询，`Load()` 只注册 `CustomerPatch`。

## 开发说明

本项目最初使用 Claude Code 辅助制作。相关本地配置不参与插件运行，也不纳入公开仓库或安装 ZIP。

Release 中的安装 ZIP 包含编译好的 `MystiaRecommendation.dll` 和完整 `Data/` 目录，目的是让其他端或不具备构建环境的使用者可以快速安装。

建议调试时查看 BepInEx 日志中的 `[MystiaRec]` 记录。日志会输出稀客、座位、订单标签、预算、解锁状态、所选推荐分支、候选数量和最终方案。

## 数据来源与许可证

[AGPL-3.0-only](LICENSE)

数据来源于东方夜雀食堂小助手（[izakaya.cc](https://izakaya.cc) / [AnYiEE/touhou-mystia-izakaya-assistant](https://github.com/AnYiEE/touhou-mystia-izakaya-assistant)），推荐引擎、运行时检测和 UI 逻辑为本项目独立实现。详见 [NOTICE](NOTICE)。

《东方夜雀食堂》及相关权利归各自权利人所有。本项目为非官方社区工具。

# MystiaRecommendation — 东方夜雀食堂稀客推荐插件

当稀客到店时，自动显示最优套餐推荐（料理 + 酒水 + 食材 + 厨具），帮助你快速做出最佳搭配。

## 功能特性

- **自动推荐**：稀客到店时，结合口味偏好、羁绊等级、厨具库存、食材库存、流行趋势，实时计算最优套餐
- **智能解锁检测**：通过读取游戏运行时数据精准判断料理解锁状态
  - 自带食谱（self）
  - 羁绊等级解锁（bond，含经验满但未升级的边界情况）
  - 玩家等级解锁（levelup，含区域限定）
  - 任务/商店/特殊（quest/shop/special，结合区域开放天数过滤）
- **区域日程系统**：根据游戏内日期判断各区区域是否开放，过滤尚未解锁区域的食谱和角色
- **多稀客支持**：支持多个稀客同时到店，每位独立显示推荐卡片
- **可配置**：F1 打开配置面板，调整显示位置、推荐策略、DLC 选择等

## 技术栈

- **框架**: BepInEx 6 (IL2CPP)
- **运行时**: .NET 6
- **Hook**: HarmonyX
- **UI**: Unity IMGUI (OnGUI)
- **反射**: IL2CPP 运行时反射（读取游戏状态）

## 热键

| 热键 | 功能 |
|------|------|
| F1 | 打开配置面板 |
| F2 | 快速开关叠加显示 |
| F5 | 手动刷新推荐（同时清除缓存重新检测解锁状态） |

## 项目结构

```
MystiaRecommendation/
├── README.md
├── MystiaRecommendation.csproj
├── Plugin.cs                         # 插件主入口 + 解锁检测引擎
├── Patches/
│   ├── CustomerPatch.cs              # 稀客到店/离开事件 Hook
│   └── InventoryPatch.cs             # 背包相关 Hook
├── Engine/
│   ├── CustomerDataEngine.cs         # 稀客数据加载（69个角色）
│   ├── RecipeMatcher.cs              # 推荐匹配算法
│   ├── GameStateCache.cs             # 游戏状态缓存
│   └── SimpleJson.cs                 # 轻量 JSON 解析
├── UI/
│   ├── GUIBehaviour.cs               # IMGUI 生命周期管理
│   └── OverlayRenderer.cs            # 推荐卡片渲染
├── Config/
│   └── PluginConfig.cs               # 配置项定义
├── Data/
│   ├── customers_rare.json           # 69个稀客完整数据（含主场区域）
│   ├── recipes.json                  # 190个料理数据（含解锁条件）
│   ├── beverages.json                # 酒水数据
│   ├── ingredients.json              # 食材数据
│   └── area_unlock_schedule.json     # 各区区域解锁日程（可手动修改）
└── Scripts/
    ├── merge_recipe_unlock.py        # 从 TS 源码合并食谱解锁条件
    └── merge_missing_customers.py   # 从 TS 源码补齐缺失角色
```

## 解锁检测机制

插件启动时自动读取游戏运行时状态，通过反射调用游戏 API：

| 解锁类型 | 检测方法 | 说明 |
|---------|---------|------|
| Self | 直接判断 | 初始自带 |
| Bond | `RunTimeAlbum.GetCharacterKizuna(Int32)` | 羁绊等级 + 经验满检测 |
| LevelUp | `RunTimePlayerData.Level` | 玩家等级 + 区域天数过滤 |
| Quest/Shop/Special | `RunTimeStorage.HaveRecipe(Int32)` | 结合区域开放天数 |

**区域日程**：从 `area_unlock_schedule.json` 加载，当前包含妖怪兽道(day1)、人间之里(day17)、博丽神社(day34)、红魔馆(day48)、迷途竹林(day69)。Quest 食谱、区域限定的 LevelUp 食谱、以及角色主场所在区域未开放时，对应食谱均不会自动解锁。

## 安装

1. 安装 BepInEx 6 IL2CPP 到游戏目录
2. 将 `MystiaRecommendation.dll` 放入 `BepInEx/plugins/`
3. 将 `Data/` 目录复制到 `BepInEx/plugins/Data/`
4. 启动游戏，进入营业后按 F5 刷新推荐

## 配置

游戏内按 **F1** 打开配置面板，可调整：

- 显示位置、透明度、字体大小
- 推荐数量上限、优化目标
- 流行趋势权重、额外食材策略
- DLC 筛选、特定料理/酒水/食材隐藏
- 标签显示、符卡信息、热键绑定

## 更新攻略数据

如果发现区域解锁日程不准确，可以直接编辑 `Data/area_unlock_schedule.json`，修改 `absoluteDay` 值即可，无需重新编译插件。

## 许可证

本项目以 **AGPL-3.0-only** 发布，完整文本见 [LICENSE](LICENSE)。

本项目数据文件来源于以下 AGPL-3.0 开源项目：
- [blockshy/mystia-steward-companion](https://github.com/blockshy/mystia-steward-companion)
- 东方夜雀食堂小助手 ([izakaya.cc](https://izakaya.cc))

来源与授权声明见 [NOTICE](NOTICE)。

《东方夜雀食堂》及相关游戏名称、角色名称、角色形象、游戏素材、文本内容、商标标识等权利归各自权利人所有。本项目为非官方社区工具。

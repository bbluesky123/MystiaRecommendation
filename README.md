# MystiaRecommendation — 东方夜雀食堂稀客推荐插件

当稀客到店时，自动显示最优套餐推荐（料理 + 酒水 + 食材 + 厨具），帮助你快速做出搭配。

## 快速安装

1. 确保游戏已安装 **BepInEx 6 IL2CPP**（[下载](https://builds.bepis.io/projects/bepinex_be)）
2. 从 [Releases](https://github.com/bbluesky123/MystiaRecommendation/releases) 下载最新 `MystiaRecommendation-v*.zip`
3. 解压到 `<游戏目录>\BepInEx\plugins\`（最终路径应为 `...\plugins\MystiaRecommendation\MystiaRecommendation.dll`）
4. 启动游戏，进入营业后按 F5 刷新

```
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

## 热键

| 热键 | 功能 |
|------|------|
| F1 | 打开配置面板 |
| F2 | 快速开关叠加显示 |
| F5 | 手动刷新推荐（同时清除缓存） |

## 功能特性

- **自动推荐**：稀客到店时，结合口味偏好、羁绊等级、厨具库存、食材库存、流行趋势，实时计算最优套餐
- **智能解锁检测**：通过反射读取游戏运行时数据精准判断料理解锁状态
  - 自带食谱（Self）
  - 羁绊等级解锁（Bond）
  - 玩家等级解锁（LevelUp，含区域限定）
  - 任务/商店/特殊（Quest/Shop/Special，结合区域开放天数过滤）
- **区域日程系统**：根据游戏内日期判断各区区域是否开放，过滤尚未解锁区域的食谱和角色
- **多稀客支持**：支持多个稀客同时到店，每位独立显示推荐卡片
- **可配置**：F1 打开配置面板

## 配置

游戏内按 **F1** 打开配置面板，可调整：

- 显示位置、透明度、字体大小
- 推荐数量上限、优化目标
- 流行趋势权重、额外食材策略
- DLC 筛选、特定料理/酒水/食材隐藏

如果区域解锁日程不准确，可直接编辑 `Data/area_unlock_schedule.json`，修改 `absoluteDay` 值即可，无需重新编译。

## 从源码构建

```bash
git clone https://github.com/bbluesky123/MystiaRecommendation.git
cd MystiaRecommendation

# 复制本机配置模板，并将 BepInExDir 改为实际的 BepInEx 目录
cp Directory.Build.user.props.example Directory.Build.user.props

dotnet build -c Release
```

也可以通过命令行属性或环境变量指定目录，无需创建本机配置文件：

```bash
dotnet build -c Release -p:BepInExDir="D:\游戏目录\BepInEx"
MYSTIA_BEPINEX_DIR="/path/to/BepInEx" dotnet build -c Release
```

构建成功后，DLL 和 `Data/` 会自动复制到 `BepInEx/plugins/MystiaRecommendation/`。

**前置条件**：[.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)。

## 项目结构

```
MystiaRecommendation/
├── Plugin.cs                         # 插件主入口 + 解锁检测引擎
├── Directory.Build.props             # 公共构建路径规则
├── Directory.Build.user.props.example # 本机 BepInEx 路径模板
├── Patches/
│   ├── CustomerPatch.cs              # 稀客到店/离开 Hook
│   ├── InventoryPatch.cs             # 背包相关 Hook
│   └── RecipePatch.cs                # 食谱解锁 Hook
├── Engine/
│   ├── CustomerDataEngine.cs         # 稀客数据加载（69 个角色）
│   ├── RecipeMatcher.cs              # 推荐匹配算法
│   ├── GameStateCache.cs             # 游戏状态缓存
│   └── SimpleJson.cs                 # 轻量 JSON 解析
├── UI/
│   ├── GUIBehaviour.cs               # IMGUI 生命周期管理
│   └── OverlayRenderer.cs            # 推荐卡片渲染
├── Config/
│   └── PluginConfig.cs               # 配置项定义
└── Data/
    ├── customers_rare.json           # 69 个稀客完整数据
    ├── recipes.json                  # 190 个料理数据
    ├── beverages.json                # 48 个酒水数据
    ├── ingredients.json              # 70 个食材数据
    └── area_unlock_schedule.json     # 各区区域解锁日程
```

## 解锁检测机制

| 解锁类型 | 检测方法 | 说明 |
|---------|---------|------|
| Self | 直接判定 | 初始自带 |
| Bond | `RunTimeAlbum.GetCharacterKizuna(Int32)` | 羁绊等级 + 经验满边界检测 |
| LevelUp | `RunTimePlayerData.Level` | 玩家等级 + 区域天数过滤 |
| Quest/Shop/Special | `RunTimeStorage.HaveRecipe(Int32)` | 结合区域开放天数 |

区域日程：妖怪兽道(day1)、人间之里(day17)、博丽神社(day34)、红魔馆(day48)、迷途竹林(day69)。区域未开放时，相关食谱和角色均不会触发推荐。

## 许可证

[AGPL-3.0-only](LICENSE)

数据来源于东方夜雀食堂小助手（[izakaya.cc](https://izakaya.cc) / [AnYiEE/touhou-mystia-izakaya-assistant](https://github.com/AnYiEE/touhou-mystia-izakaya-assistant)），推荐引擎及解锁检测等逻辑为独立开发。详见 [NOTICE](NOTICE)。

《东方夜雀食堂》及相关权利归各自权利人所有。本项目为非官方社区工具。

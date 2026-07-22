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
├── LICENSE
├── NOTICE
├── MystiaRecommendation.csproj
├── build.py                          # 构建脚本
├── Plugin.cs                         # 插件主入口 + 解锁检测引擎
├── Patches/
│   ├── CustomerPatch.cs              # 稀客到店/离开事件 Hook
│   ├── InventoryPatch.cs             # 背包相关 Hook
│   └── RecipePatch.cs                # 食谱解锁 Hook
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
└── Data/
    ├── customers_rare.json           # 69个稀客完整数据（含主场区域）
    ├── recipes.json                  # 190个料理数据（含解锁条件）
    ├── beverages.json                # 酒水数据
    ├── ingredients.json              # 食材数据
    └── area_unlock_schedule.json     # 各区区域解锁日程（可手动修改）
```

## 在新 PC 上使用

### 前置条件

1. **.NET 6 SDK**：从 https://dotnet.microsoft.com/download/dotnet/6.0 下载安装

2. **BepInEx 6 IL2CPP**：安装到游戏目录
   - 下载 BepInEx 6 (IL2CPP x64) 并解压到游戏根目录
   - 确保 `BepInEx/core/` 和 `BepInEx/interop/` 目录中有对应的 DLL

3. **东方夜雀食堂**（Steam 版）

4. **Git**（用于克隆仓库）

### 构建安装

```bash
# 1. 克隆仓库
git clone https://github.com/bbluesky123/MystiaRecommendation.git
cd MystiaRecommendation

# 2. 编译（将游戏路径替换为你的实际路径）
python build.py "D:\steam\steamapps\common\Touhou Mystia Izakaya"

# 如果不用 Python，也可以直接用 dotnet：
# dotnet build -c Release -p:GameDir="你的游戏路径"
```

编译成功后，DLL 和 Data 文件会自动复制到 `BepInEx/plugins/MystiaRecommendation/`。

### 手动安装（已有编译好的 DLL）

1. 将 `MystiaRecommendation.dll` 放入 `<游戏目录>\BepInEx\plugins\MystiaRecommendation\`
2. 将 `Data\` 文件夹复制到 `<游戏目录>\BepInEx\plugins\MystiaRecommendation\Data\`
3. 启动游戏，进入营业后按 F5 刷新

### 更新

```bash
git pull
python build.py "你的游戏路径"
```

## 配置

游戏内按 **F1** 打开配置面板，可调整：

- 显示位置、透明度、字体大小
- 推荐数量上限、优化目标
- 流行趋势权重、额外食材策略
- DLC 筛选、特定料理/酒水/食材隐藏
- 标签显示、符卡信息、热键绑定

## 更新攻略数据

如果发现区域解锁日程不准确，可以直接编辑 `Data/area_unlock_schedule.json`，修改 `absoluteDay` 值即可，无需重新编译。

## 解锁检测机制

插件启动时自动读取游戏运行时状态，通过反射调用游戏 API：

| 解锁类型 | 检测方法 | 说明 |
|---------|---------|------|
| Self | 直接判断 | 初始自带 |
| Bond | `RunTimeAlbum.GetCharacterKizuna(Int32)` | 羁绊等级 + 经验满检测 |
| LevelUp | `RunTimePlayerData.Level` | 玩家等级 + 区域天数过滤 |
| Quest/Shop/Special | `RunTimeStorage.HaveRecipe(Int32)` | 结合区域开放天数 |

**区域日程**：从 `area_unlock_schedule.json` 加载，当前包含妖怪兽道(day1)、人间之里(day17)、博丽神社(day34)、红魔馆(day48)、迷途竹林(day69)。Quest 食谱、区域限定的 LevelUp 食谱、以及角色主场所在区域未开放时，对应食谱均不会自动解锁。

## 许可证

本项目以 **AGPL-3.0-only** 发布，完整文本见 [LICENSE](LICENSE)。

本项目数据来源于东方夜雀食堂小助手 ([izakaya.cc](https://izakaya.cc))，其源码（[AnYiEE/touhou-mystia-izakaya-assistant](https://github.com/AnYiEE/touhou-mystia-izakaya-assistant)）以 AGPL-3.0-only 发布。推荐引擎及解锁检测等逻辑均为独立开发。

来源与授权声明详见 [NOTICE](NOTICE)。

《东方夜雀食堂》及相关游戏名称、角色名称、角色形象、游戏素材、文本内容、商标标识等权利归各自权利人所有。本项目为非官方社区工具。

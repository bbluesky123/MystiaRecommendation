# 安装指南

## 前置条件

- .NET 6 SDK（用于编译插件）
- il2cppdumper（用于分析游戏类型信息）

## 第一步：安装 BepInEx 6 IL2CPP（⚠️ 必须是 IL2CPP 版本）

> **重要**：该游戏使用 IL2CPP 运行时（有 GameAssembly.dll），必须使用 IL2CPP 版的 BepInEx，**不能**使用 Mono 版。

1. 下载 BepInEx 6 Bleeding Edge (**IL2CPP**) 版本
   - 地址: https://builds.bepinex.dev/projects/bepinex_be
   - 选择文件名包含 `Unity.IL2CPP` 的版本，例如:
     `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.xxxx.zip`
   - ⚠️ **不要**选择 `Unity.Mono` 版本！

2. 解压到游戏根目录 `D:\steam\steamapps\common\Touhou Mystia Izakaya\`
   - 正确安装后，`BepInEx/core/` 目录下应包含:
     - `BepInEx.Unity.IL2CPP.dll` ← IL2CPP 版本的核心标识
     - `Il2CppInterop.Runtime.dll`
     - `Il2CppInterop.HarmonySupport.dll`

3. 运行一次游戏，等待 BepInEx 初始化完成
   - 会自动生成 `BepInEx/interop/` 目录
   - 关闭游戏

4. **如何验证安装正确**:
   - 检查 `BepInEx/core/` 是否包含 `BepInEx.Unity.IL2CPP.dll`
   - 检查是否生成了 `BepInEx/interop/` 目录
   - 检查 `BepInEx/LogOutput.log` 是否有 BepInEx 加载日志

## 第二步：运行 il2cppdumper

1. 下载 il2cppdumper: https://github.com/Perfare/Il2CppDumper/releases

2. 运行:
   ```
   Il2CppDumper.exe GameAssembly.dll "Touhou Mystia Izakaya_Data/il2cpp_data/Metadata/global-metadata.dat" dump_output
   ```

3. 在 `dump_output/dump.cs` 中搜索关键类名:
   - 搜索 `稀客` 找到稀客相关类
   - 搜索 `Customer` 找到英文类名
   - 搜索 `Arrive`、`Enter`、`Spawn` 找到到达方法
   - 搜索 `RecipeData`、`FoodTag`、`BeverageTag` 等已知类

4. 将 `dump_output/Assembly-CSharp.dll` 复制到 `BepInEx/interop/`

## 第三步：填充代码中的 TODO

根据 dump 结果，修改以下文件中的 TODO 注释:

- `Patches/CustomerPatch.cs` - 稀客到店/离开 Hook
- `Patches/InventoryPatch.cs` - 背包读取 Hook
- `Patches/RecipePatch.cs` - 食谱/烹饪系统 Hook

## 第四步：编译插件

```bash
cd D:\new\MystiaRecommendation
dotnet build -c Release
```

编译产物会自动复制到 `BepInEx/plugins/MystiaRecommendation/`

## 第五步：（可选）安装 ConfigurationManager

1. 下载 BepInEx.ConfigurationManager
2. 放入 `BepInEx/plugins/`
3. 游戏内按 F1 打开配置面板

## 第六步：运行游戏

启动游戏，插件会自动加载。当稀客到店时会显示推荐信息。

## 热键

| 热键 | 功能 |
|------|------|
| F1   | 打开配置面板（需安装 ConfigurationManager） |
| F2   | 快速开关叠加显示 |
| F5   | 手动刷新推荐 |

## 常见问题

### Q: interop 目录没有生成？
A: 检查是否安装了正确版本的 BepInEx。该游戏必须使用 **IL2CPP** 版本，不能用 Mono 版本。查看 `BepInEx/core/` 目录，如果有 `BepInEx.Unity.Mono.dll` 说明装错了，需要换成 `BepInEx.Unity.IL2CPP.dll`。

### Q: 游戏启动后没有反应？
A: 检查 `BepInEx/LogOutput.log` 文件，查看是否有错误信息。确保 `winhttp.dll` 和 `doorstop_config.ini` 已正确放置在游戏根目录。

### Q: 找不到 Assembly-CSharp.dll？
A: 需要先运行一次安装了 BepInEx IL2CPP 的游戏，它会在 `BepInEx/interop/` 中自动生成 IL2CPP 类型的托管包装程序集。
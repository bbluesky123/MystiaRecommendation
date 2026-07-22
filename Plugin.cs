using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using MystiaRecommendation.Engine;
using MystiaRecommendation.UI;
using MystiaRecommendation.Config;
using System.Linq;

namespace MystiaRecommendation;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static Plugin Instance { get; private set; }
    internal static Harmony Harmony { get; private set; }
    internal static CustomerDataEngine DataEngine { get; private set; }
    internal static RecipeMatcher Matcher { get; private set; }
    internal static PluginConfig PluginConfig { get; private set; }
    internal static string DataDirectory { get; private set; }

    // 多稀客支持：唯一ID -> 推荐信息
    internal static Dictionary<int, CustomerRecommendation> ActiveRecommendations { get; private set; } = new();
    private static int _nextRecommendId = 0;
    internal static int GetNextRecommendId() => _nextRecommendId++;

    public override void Load()
    {
        Instance = this;
        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        PluginConfig = new PluginConfig(base.Config);

        DataDirectory = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "Data"
        );

        Log.LogInfo("正在加载稀客数据...");
        DataEngine = new CustomerDataEngine(DataDirectory);

        Log.LogInfo("正在加载料理/酒水数据...");
        RecipeDatabase.LoadFromDirectory(DataDirectory);

        Log.LogInfo("正在初始化推荐引擎...");
        Matcher = new RecipeMatcher(DataEngine);

        Log.LogInfo("正在注册 Harmony 补丁...");
        Harmony.PatchAll(typeof(Patches.CustomerPatch));

        Log.LogInfo("正在注册 UI 渲染组件...");
        UI.GUIBehaviour.Create();

        Log.LogInfo($"[{MyPluginInfo.PLUGIN_NAME}] v{MyPluginInfo.PLUGIN_VERSION} 加载完成！");
        Log.LogInfo($"已加载 {DataEngine.CustomerCount} 个稀客, " +
            $"{RecipeDatabase.RecipeCount} 个料理, " +
            $"{RecipeDatabase.BeverageCount} 个酒水");
    }

    /// <summary>
    /// 稀客到店时调用，支持多个稀客同时到店
    /// </summary>
    internal static void OnCustomerArrived(string customerName, string reqFoodTag, string reqBevTag, int deskCode, int orderBudget = -1)
    {
        bool hasCustomer = DataEngine.HasCustomer(customerName);
        var customer = hasCustomer ? DataEngine.GetCustomer(customerName) : null;
        int maxBudget = orderBudget > 0
            ? (customer != null ? System.Math.Min(orderBudget, customer.MaxBudget) : orderBudget)
            : (customer != null ? customer.MaxBudget : 999);

        Instance?.Log.LogInfo($"[MystiaRec] 开始推荐: {customerName} 座位{deskCode} (预算:{maxBudget})");
        Instance?.Log.LogInfo($"[MystiaRec] 请求: 食物={reqFoodTag}, 酒水={reqBevTag}");
        if (!hasCustomer)
            Instance?.Log.LogWarning($"[MystiaRec] 稀客 [{customerName}] 不在数据表中，使用仅按订单匹配的推荐");

        // 主动查询游戏状态
        var unlockedRecipes = GetUnlockedRecipes();
        var unlockedBeverages = GetUnlockedBeverages();
        var availableCookers = GetAvailableCookers();
        var availableIngredients = GetAvailableIngredients();
        var ingredientStocks = GetIngredientStocks();
        var popularTrend = GetPopularTrend();

        Instance?.Log.LogInfo($"[MystiaRec] 已解锁: 料理={unlockedRecipes.Count}, 酒水={unlockedBeverages.Count}, 当前厨具={availableCookers.Count}, 食材={availableIngredients.Count}");
        if (unlockedRecipes.Count > 0)
            Instance?.Log.LogInfo("[MystiaRec] 当前料理: " + string.Join(",", unlockedRecipes.Where(n => !int.TryParse(n, out _))));
        if (unlockedBeverages.Count > 0)
            Instance?.Log.LogInfo("[MystiaRec] 当前酒水: " + string.Join(",", unlockedBeverages.Where(n => !int.TryParse(n, out _))));
        if (availableCookers.Count > 0)
            Instance?.Log.LogInfo("[MystiaRec] 当前厨具: " + string.Join(",", availableCookers));
        if (popularTrend.HasAny)
            Instance?.Log.LogInfo($"[MystiaRec] 流行趋势: 食物喜爱={string.Join(",", popularTrend.LikeFoodTags)} 食物厌恶={string.Join(",", popularTrend.HateFoodTags)} 酒水喜爱={string.Join(",", popularTrend.LikeBeverageTags)} 酒水厌恶={string.Join(",", popularTrend.HateBeverageTags)}");

        // 计算推荐
        var recommendations = hasCustomer
            ? Matcher.CalculateByRequestTags(
                customerName, reqFoodTag, reqBevTag, maxBudget,
                unlockedRecipes, unlockedBeverages, availableIngredients, availableCookers, ingredientStocks, popularTrend)
            : Matcher.CalculateUnknownByRequestTags(
                reqFoodTag, reqBevTag, maxBudget,
                unlockedRecipes, unlockedBeverages, availableIngredients, popularTrend);
        // 只保留前2个推荐
        if (recommendations.Count > 2)
            recommendations = recommendations.Take(2).ToList();

        string status = "";
        if (!hasCustomer)
            status = recommendations.Count == 0 ? "稀客数据未收录，无可用订单方案" : "稀客数据未收录，仅按订单匹配";
        else if (recommendations.Count == 0)
            status = "无可用方案";

        UpsertRecommendationCard(customerName, deskCode, reqFoodTag, reqBevTag, recommendations, status);

        Instance?.Log.LogInfo($"[MystiaRec] 推荐完成: {customerName} 座位{deskCode} {recommendations.Count} 个方案");
        foreach (var rec in recommendations)
        {
            string ingredients = string.Join(", ", rec.Ingredients);
            Instance?.Log.LogInfo($"  [{rec.ExpectedRating}] 料理:{rec.RecipeName}({rec.Score}) + 酒水:{rec.BeverageName} (账面价:{rec.TotalPrice})");
            Instance?.Log.LogInfo($"    酒水标签: {string.Join(", ", rec.BeverageTags)}");
            Instance?.Log.LogInfo($"    厨具: {rec.RequiredCooker}");
            Instance?.Log.LogInfo($"    食材: {ingredients}");
            Instance?.Log.LogInfo($"    标签: {string.Join(", ", rec.RecipeTags)}");
        }
    }

    internal static void OnCustomerPending(string customerName, string reqFoodTag, string reqBevTag, int deskCode, string statusMessage)
    {
        if (string.IsNullOrEmpty(customerName)) return;

        // 防竞态：刚离场的座位不重新创建卡片
        if (IsDeskRecentlyDeparted(deskCode))
        {
            Instance?.Log.LogInfo($"[MystiaRec] 跳过离场冷却中的卡片: {customerName} 座位{deskCode}");
            return;
        }

        bool hasCompletedCard = ActiveRecommendations.Values.Any(cr =>
            cr.DeskCode == deskCode &&
            cr.CustomerName == customerName &&
            cr.Recommendations != null &&
            cr.Recommendations.Count > 0);
        if (hasCompletedCard) return;
        Instance?.Log.LogInfo($"[MystiaRec] 显示等待卡片: {customerName} 座位{deskCode} {statusMessage}");
        UpsertRecommendationCard(customerName, deskCode, reqFoodTag, reqBevTag, new List<Recommendation>(), statusMessage);
    }

    private static void UpsertRecommendationCard(
        string customerName,
        int deskCode,
        string reqFoodTag,
        string reqBevTag,
        List<Recommendation> recommendations,
        string statusMessage)
    {
        // 清除该稀客之前的推荐（同名同座位的旧推荐）
        var toRemove = new List<int>();
        foreach (var kv in ActiveRecommendations)
        {
            if (kv.Value.DeskCode == deskCode)
                toRemove.Add(kv.Key);
        }
        foreach (var k in toRemove) ActiveRecommendations.Remove(k);

        // 存储到多稀客推荐字典（用唯一ID）
        int rid = GetNextRecommendId();
        ActiveRecommendations[rid] = new CustomerRecommendation
        {
            CustomerName = customerName,
            DeskCode = deskCode,
            ReqFoodTag = reqFoodTag,
            ReqBevTag = reqBevTag,
            Recommendations = recommendations,
            StatusMessage = statusMessage,
            Timestamp = UnityEngine.Time.time
        };
    }

    /// <summary>
    /// 稀客离店时调用
    /// </summary>
    internal static void OnCustomerLeft(int deskCode)
    {
        var keys = ActiveRecommendations
            .Where(kv => kv.Value.DeskCode == deskCode)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keys)
        {
            ActiveRecommendations.Remove(key);
            Instance?.Log.LogInfo($"[MystiaRec] 稀客离店: 座位{deskCode}");
        }

        // 防止离场后 OnGetGuestName 钩子重新创建卡片
        _recentlyDepartedDesks.Add(deskCode);
        _lastDepartTime = UnityEngine.Time.time;
    }

    private static HashSet<int> _recentlyDepartedDesks = new();
    private static float _lastDepartTime = 0f;
    private const float DEPART_COOLDOWN = 5f;

    internal static bool IsDeskRecentlyDeparted(int deskCode)
    {
        if (UnityEngine.Time.time - _lastDepartTime > DEPART_COOLDOWN)
            _recentlyDepartedDesks.Clear();
        return _recentlyDepartedDesks.Contains(deskCode);
    }

    internal static void ClearDeskIfOccupiedByOther(int deskCode, string currentCustomerName)
    {
        if (deskCode < 0) return;
        var keys = ActiveRecommendations
            .Where(kv => kv.Value.DeskCode == deskCode && kv.Value.CustomerName != currentCustomerName)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keys)
        {
            var card = ActiveRecommendations[key];
            ActiveRecommendations.Remove(key);
            Instance?.Log.LogInfo($"[MystiaRec] 座位换客，清理旧卡片: {card.CustomerName} -> {currentCustomerName} 座位{deskCode}");
        }
    }

    /// <summary>
    /// 清理所有推荐（场景切换时），同时重置当晚缓存
    /// </summary>
    internal static void ClearAllRecommendations()
    {
        ActiveRecommendations.Clear();
        _cachedUnlockedRecipes = null;
        _cachedAvailableCookers = null;
        _cachedBondData = null;
        _cachedPlayerLevel = -1;
        _cachedAbsoluteDay = -1;
    }

    private static HashSet<string> _cachedUnlockedRecipes;
    private static HashSet<string> _cachedAvailableCookers;
    private static Dictionary<string, (int level, int currentExp, int maxExp)> _cachedBondData;
    private static int _cachedPlayerLevel = -1;

    /// <summary>
    /// 羁绊数据：level=当前等级, currentExp=当前经验值, maxExp=当前等级的经验上限
    /// 当 currentExp >= maxExp 时表示经验已满但等级还未提升（料理已解锁）
    /// </summary>
    private static bool IsBondExpCapped((int level, int currentExp, int maxExp) bond)
        => bond.maxExp > 0 && bond.currentExp >= bond.maxExp;

    /// <summary>
    /// 手动刷新所有活跃卡片的推荐（快捷键触发）。
    /// 重新读取游戏状态并重新计算推荐，适用于稀客长时间等待后仍需查看推荐的场景。
    /// </summary>
    internal static void RefreshActiveRecommendations()
    {
        _cachedUnlockedRecipes = null;
        _cachedBondData = null;
        _cachedPlayerLevel = -1;
        _cachedAbsoluteDay = -1;
        GetUnlockedRecipes();

        var cards = ActiveRecommendations.ToList();
        foreach (var kv in cards)
        {
            var card = kv.Value;
            // 跳过没有有效标签的卡片（等待订单状态）
            if (string.IsNullOrEmpty(card.ReqFoodTag) && string.IsNullOrEmpty(card.ReqBevTag))
                continue;

            Instance?.Log?.LogInfo($"[MystiaRec] 刷新推荐: {card.CustomerName} 座位{card.DeskCode}");
            OnCustomerArrived(card.CustomerName, card.ReqFoodTag ?? "", card.ReqBevTag ?? "",
                card.DeskCode);
        }

        if (cards.Count == 0)
            Instance?.Log?.LogInfo("[MystiaRec] 刷新推荐: 无活跃卡片");
    }

    /// <summary>
    /// 基于 from 解锁条件判断已解锁的料理（每晚首次调用后缓存）
    /// Bond/LevelUp/Self 通过运行时API精确判断，Quest/Shop/Special 回退到 HaveRecipe
    /// </summary>
    private static HashSet<string> GetUnlockedRecipes()
    {
        if (_cachedUnlockedRecipes != null)
            return _cachedUnlockedRecipes;

        var result = new HashSet<string>();
        var bondZeroRecipes = new List<string>();   // 角色羁绊为0的食谱
        var bondMissingId = new List<string>();      // 找不到角色ID的食谱
        var fallbackRecipes = new List<string>();    // 回退到HaveRecipe的食谱
        try
        {
            int playerLevel = GetPlayerLevel();
            var bondLevels = GetBondLevels();

            int totalCount = 0;
            int selfCount = 0, bondCount = 0, levelupCount = 0, fallbackCount = 0;
            int bondSkippedCount = 0; // 羁绊满足但等级不足

            foreach (var info in RecipeDatabase.GetAllRecipes())
            {
                totalCount++;
                var unlock = info.Unlock;
                bool unlocked = false;

                if (unlock == null || unlock.Type == UnlockType.Unknown)
                {
                    // 无解锁数据 → HaveRecipe 兜底
                    unlocked = HaveRecipeSafe(info.Id);
                    if (unlocked) fallbackCount++;
                }
                else switch (unlock.Type)
                {
                    case UnlockType.Self:
                        unlocked = true;
                        selfCount++;
                        break;
                    case UnlockType.Bond:
                    {
                        int currentLevel = 0;
                        bool expCapped = false;
                        bool hasBond = false;
                        if (!string.IsNullOrEmpty(unlock.BondName)
                            && bondLevels.TryGetValue(unlock.BondName, out var bond))
                        {
                            hasBond = true;
                            currentLevel = bond.level;
                            expCapped = IsBondExpCapped(bond);
                        }
                        if (hasBond)
                        {
                            // 检查角色所在区域是否已解锁（无法对话则食谱未解锁）
                            if (!IsCharacterAccessible(unlock.BondName, GetAbsoluteDay()))
                            {
                                bondSkippedCount++;
                                fallbackRecipes.Add($"{info.Name}(角色区域未解锁:{unlock.BondName})");
                            }
                            // 等级达标 或 经验已满等待升级（但料理已解锁）
                            else if (currentLevel >= unlock.BondLevel ||
                                (expCapped && currentLevel == unlock.BondLevel - 1))
                            {
                                unlocked = true;
                                bondCount++;
                            }
                            else if (currentLevel > 0 || expCapped)
                            {
                                bondSkippedCount++;
                            }
                            else
                            {
                                bondZeroRecipes.Add($"{info.Name}(需要{unlock.BondName}Lv{unlock.BondLevel},当前{currentLevel})");
                            }
                        }
                        else
                        {
                            bondMissingId.Add($"{info.Name}(需要{unlock.BondName},无ID)");
                        }
                        break;
                    }
                    case UnlockType.LevelUp:
                        if (!string.IsNullOrEmpty(unlock.Area))
                        {
                            // 区域锁定 → 先检查区域是否开放
                            if (!IsQuestAreaAccessible(unlock.Area, GetAbsoluteDay()))
                            {
                                fallbackRecipes.Add($"{info.Name}(区域未解锁:{unlock.Area})");
                            }
                            else
                            {
                                unlocked = HaveRecipeSafe(info.Id);
                                if (unlocked) fallbackCount++;
                                else fallbackRecipes.Add($"{info.Name}(LevelUp:{unlock.Area})");
                            }
                        }
                        else if (playerLevel >= unlock.RequiredLevel)
                        {
                            unlocked = true;
                            levelupCount++;
                        }
                        break;
                    case UnlockType.QuestOrEvent:
                        // 任务 → 先检查区域是否已解锁，再使用 HaveRecipe
                        if (!string.IsNullOrEmpty(unlock.Description)
                            && !IsQuestAreaAccessible(unlock.Description, GetAbsoluteDay()))
                        {
                            // 区域未解锁 → 食谱必定未解锁（过滤假阳性！）
                            fallbackRecipes.Add($"{info.Name}(区域未解锁)");
                        }
                        else
                        {
                            unlocked = HaveRecipeSafe(info.Id);
                            if (unlocked) fallbackCount++;
                            else fallbackRecipes.Add($"{info.Name}(QuestOrEvent)");
                        }
                        break;
                    case UnlockType.Shop:
                    case UnlockType.Special:
                        // 商店/特殊 → HaveRecipe 兜底
                        unlocked = HaveRecipeSafe(info.Id);
                        if (unlocked) fallbackCount++;
                        else fallbackRecipes.Add($"{info.Name}({unlock?.Type})");
                        break;
                    default:
                        // 未知类型 → HaveRecipe 兜底
                        unlocked = HaveRecipeSafe(info.Id);
                        if (unlocked) fallbackCount++;
                        else fallbackRecipes.Add($"{info.Name}(unknown)");
                        break;
                }

                if (unlocked)
                    result.Add(info.Name);
            }

            Instance?.Log?.LogInfo($"[MystiaRec] 料理诊断(当晚首次): 总数={totalCount}, " +
                $"self={selfCount}, bond={bondCount}(跳过{bondSkippedCount}个等级不足), levelup={levelupCount}, fallback={fallbackCount}, " +
                $"总计解锁={result.Count}, 玩家等级={playerLevel}, 绝对天数={GetAbsoluteDay()}");
            Instance?.Log?.LogInfo($"[MystiaRec] === 已解锁料理列表({result.Count}个) ===\n[{string.Join(", ", result.OrderBy(n => n))}]");
            if (bondMissingId.Count > 0)
                Instance?.Log?.LogInfo($"[MystiaRec] 缺少角色ID({bondMissingId.Count}个): [{string.Join("; ", bondMissingId.Take(10))}]");
            if (bondZeroRecipes.Count > 0)
                Instance?.Log?.LogInfo($"[MystiaRec] 角色羁绊为0({bondZeroRecipes.Count}个): [{string.Join("; ", bondZeroRecipes.Take(10))}]");
            if (fallbackRecipes.Count > 0)
                Instance?.Log?.LogInfo($"[MystiaRec] HaveRecipe未解锁({fallbackRecipes.Count}个): [{string.Join("; ", fallbackRecipes.Take(10))}]");
        }
        catch (System.Exception e)
        {
            Instance?.Log?.LogWarning("[MystiaRec] GetUnlockedRecipes: " + e.Message);
        }
        _cachedUnlockedRecipes = result;
        return result;
    }

    /// <summary>
    /// 读取玩家等级（缓存）
    /// </summary>
    private static int GetPlayerLevel()
    {
        if (_cachedPlayerLevel >= 0) return _cachedPlayerLevel;
        try
        {
            var levelProp = typeof(GameData.RunTime.Common.RunTimePlayerData).GetProperty("Level",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (levelProp != null)
            {
                var val = levelProp.GetValue(null);
                _cachedPlayerLevel = System.Convert.ToInt32(val);
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log?.LogWarning("[MystiaRec] GetPlayerLevel: " + e.Message);
        }
        if (_cachedPlayerLevel < 0) _cachedPlayerLevel = 0;
        return _cachedPlayerLevel;
    }

    /// <summary>
    /// 读取游戏绝对天数（缓存）
    /// 从 GameDate 的 Month 和 ActuallDay 计算：Month*30 + ActuallDay
    /// </summary>
    private static int _cachedAbsoluteDay = -1;
    private static int GetAbsoluteDay()
    {
        if (_cachedAbsoluteDay >= 0) return _cachedAbsoluteDay;
        try
        {
            var rtpType = typeof(GameData.RunTime.Common.RunTimePlayerData);
            var dateProp = rtpType.GetProperty("Date",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (dateProp != null)
            {
                var dateVal = dateProp.GetValue(null);
                if (dateVal != null)
                {
                    var dateType = dateVal.GetType();
                    var monthField = dateType.GetField("month",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var monthProp = dateType.GetProperty("Month",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var dayField = dateType.GetField("day",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    int month = System.Convert.ToInt32(monthField?.GetValue(dateVal) ?? monthProp?.GetValue(dateVal) ?? 1);
                    int day = System.Convert.ToInt32(dayField?.GetValue(dateVal) ?? 1);
                    _cachedAbsoluteDay = (month - 1) * 30 + day;
                }
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log?.LogWarning("[MystiaRec] GetAbsoluteDay: " + e.Message);
        }
        if (_cachedAbsoluteDay < 0) _cachedAbsoluteDay = 1;
        return _cachedAbsoluteDay;
    }

    // 各区域解锁的绝对天数阈值，从 JSON 加载（可手动修改）
    private static Dictionary<string, int> _areaUnlockDays = null;
    private static Dictionary<string, int> AreaUnlockDays
    {
        get
        {
            if (_areaUnlockDays != null) return _areaUnlockDays;
            _areaUnlockDays = new();
            try
            {
                var path = Path.Combine(DataDirectory, "area_unlock_schedule.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var obj = System.Text.Json.Nodes.JsonNode.Parse(json);
                    var areas = obj["areas"];
                    if (areas != null)
                    {
                        foreach (var area in areas.AsObject())
                        {
                            int absDay = (int)(area.Value?["absoluteDay"]?.GetValue<int>() ?? 999);
                            _areaUnlockDays[area.Key] = absDay;
                        }
                        Instance?.Log?.LogInfo($"[MystiaRec] 区域解锁日程已加载: {_areaUnlockDays.Count}个区域");
                    }
                }
                else
                    Instance?.Log?.LogWarning("[MystiaRec] area_unlock_schedule.json 未找到");
            }
            catch (System.Exception e)
            {
                Instance?.Log?.LogWarning("[MystiaRec] 加载区域解锁日程失败: " + e.Message);
            }
            if (_areaUnlockDays.Count == 0)
            {
                // 回退硬编码
                _areaUnlockDays = new() { { "妖怪兽道", 1 }, { "人间之里", 17 }, { "博丽神社", 34 }, { "红魔馆", 48 }, { "迷途竹林", 69 } };
            }
            return _areaUnlockDays;
        }
    }

    /// <summary>
    /// 从 quest 描述中提取区域名，判断该区域是否已解锁
    /// 例如 "地区【博丽神社】支线任务" → 博丽神社 → 34天
    /// </summary>
    private static bool IsQuestAreaAccessible(string description, int currentDay)
    {
        if (string.IsNullOrEmpty(description)) return true; // 无区域信息，不限制
        foreach (var kv in AreaUnlockDays)
        {
            if (description.Contains(kv.Key))
                return currentDay >= kv.Value;
        }
        return true; // 未识别的区域，不限制（避免假阴性）
    }

    /// <summary>
    /// 检查角色的主场（白天固定出现地点）是否已解锁
    /// places[0] 是角色的白天所在区域，其他为夜晚可能出现区域
    /// </summary>
    private static bool IsCharacterAccessible(string characterName, int currentDay)
    {
        var customer = DataEngine.GetCustomer(characterName);
        if (customer == null || customer.places == null || customer.places.Count == 0)
            return true; // 无数据，不限制
        string homeArea = customer.places[0];
        if (AreaUnlockDays.TryGetValue(homeArea, out int unlockDay))
            return currentDay >= unlockDay;
        return true; // 未知区域，不限制
    }

    /// <summary>
    /// 读取所有相关角色的羁绊数据（缓存）
    /// 返回: name → (level, currentExp, maxExp)
    /// </summary>
    private static Dictionary<string, (int level, int currentExp, int maxExp)> GetBondLevels()
    {
        if (_cachedBondData != null) return _cachedBondData;

        var result = new Dictionary<string, (int level, int currentExp, int maxExp)>();
        try
        {
            // 收集所有需要查询的羁绊角色名
            var bondNames = new HashSet<string>();
            foreach (var info in RecipeDatabase.GetAllRecipes())
            {
                if (info.Unlock?.Type == UnlockType.Bond && !string.IsNullOrEmpty(info.Unlock.BondName))
                    bondNames.Add(info.Unlock.BondName);
            }

            var asm = typeof(NightScene.GuestManagementUtility.SpecialGuestsController).Assembly;

            // === 构建 name→characterId 映射 ===
            // 来源1: customers_rare.json (已知的稀客)
            var nameToId = new Dictionary<string, int>();
            foreach (var name in bondNames)
            {
                var customer = DataEngine.GetCustomer(name);
                if (customer != null)
                    nameToId[name] = customer.id;
            }

            // 来源2: DataBaseCharacterData.SpecialGuest 字典 (label→SpecialGuest, 包含Name)
            var missingNames = bondNames.Where(n => !nameToId.ContainsKey(n)).ToList();
            if (missingNames.Count > 0)
            {
                try
                {
                    var charDataType = asm.GetTypes().FirstOrDefault(t => t.Name == "DataBaseCharacterData");
                    if (charDataType == null)
                    {
                        var allAsms = System.AppDomain.CurrentDomain.GetAssemblies();
                        foreach (var a in allAsms)
                        {
                            charDataType = a.GetTypes().FirstOrDefault(t => t.Name == "DataBaseCharacterData");
                            if (charDataType != null) break;
                        }
                    }
                    if (charDataType != null)
                    {
                        var instProp = charDataType.GetProperty("Instance",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var instance = instProp?.GetValue(null);
                        if (instance != null)
                        {
                            foreach (var propName in new[] { "SpecialGuest", "MappedSpecialGuest" })
                            {
                                var prop = charDataType.GetProperty(propName,
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (prop == null) continue;
                                var dict = prop.GetValue(instance) as System.Collections.IDictionary;
                                if (dict == null || dict.Count == 0) continue;

                                // 遍历字典，找到显示名匹配的条目
                                foreach (System.Collections.DictionaryEntry kv in dict)
                                {
                                    var guestObj = kv.Value;
                                    if (guestObj == null) continue;
                                    string displayName = "";
                                    try
                                    {
                                        var nameProp = guestObj.GetType().GetProperty("Name",
                                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                        displayName = nameProp?.GetValue(guestObj)?.ToString() ?? "";
                                    }
                                    catch { }

                                    if (!string.IsNullOrEmpty(displayName) && missingNames.Contains(displayName))
                                    {
                                        // 找这个SpecialGuest的characterId
                                        int charId = -1;
                                        try
                                        {
                                            var idProp = guestObj.GetType().GetProperty("CharacterId",
                                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                            if (idProp != null)
                                                charId = System.Convert.ToInt32(idProp.GetValue(guestObj));
                                            if (charId < 0)
                                            {
                                                var idProp2 = guestObj.GetType().GetProperty("Id",
                                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                                if (idProp2 != null)
                                                    charId = System.Convert.ToInt32(idProp2.GetValue(guestObj));
                                            }
                                        }
                                        catch { }

                                        if (charId >= 0)
                                        {
                                            nameToId[displayName] = charId;
                                            Instance?.Log?.LogInfo($"  ✓ {displayName} → characterId={charId}");
                                        }
                                        else
                                        {
                                            Instance?.Log?.LogInfo($"  ? {displayName} 的characterId未找到, key={kv.Key}");
                                        }
                                    }
                                }
                                break; // 成功读取一个字典就退出
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Instance?.Log?.LogWarning("[MystiaRec] 角色ID映射探查异常: " + ex.Message);
                }
            }

            Instance?.Log?.LogInfo($"[MystiaRec] 羁绊查询: {bondNames.Count}个角色, 其中{nameToId.Count}个有characterId" +
                (missingNames.Count > 0 ? $", 缺失{missingNames.Count}个: [{string.Join(", ", missingNames)}]" : ""));

            // === 使用 GetCharacterKizuna(Int32) 查询 ===
            var albumType = asm.GetTypes().FirstOrDefault(t => t.Name == "RunTimeAlbum");
            if (albumType != null)
            {
                var intMethod = albumType.GetMethod("GetCharacterKizuna",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new[] { typeof(int), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() },
                    null);

                if (intMethod != null)
                {
                    int foundCount = 0;
                    foreach (var kv in nameToId)
                    {
                        try
                        {
                            var args = new object[] { kv.Value, 0, 0 };
                            // retVal=currentExp, args[1]=maxExp, args[2]=level
                            var ret = intMethod.Invoke(null, args);
                            int currentExp = System.Convert.ToInt32(ret);
                            int maxExp = System.Convert.ToInt32(args[1]);
                            int level = System.Convert.ToInt32(args[2]);
                            if (currentExp == -1) { level = 0; currentExp = 0; maxExp = 0; }
                            result[kv.Key] = (level, currentExp, maxExp);
                            if (level > 0) foundCount++;
                        }
                        catch { result[kv.Key] = (0, 0, 0); }
                    }
                    Instance?.Log?.LogInfo($"[MystiaRec] 羁绊等级(Int32): 查询{nameToId.Count}个, {foundCount}个有羁绊等级");
                }
            }

            // 确保结果中每个角色都有值
            foreach (var name in bondNames)
            {
                if (!result.ContainsKey(name))
                    result[name] = (0, 0, 0);
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log?.LogWarning("[MystiaRec] GetBondLevels: " + e.Message);
        }
        _cachedBondData = result;
        return result;
    }

    /// <summary>
    /// HaveRecipe 安全包装（静默处理异常）
    /// </summary>
    private static bool HaveRecipeSafe(int recipeId)
    {
        try { return GameData.RunTime.Common.RunTimeStorage.HaveRecipe(recipeId); }
        catch { return false; }
    }

    /// <summary>
    /// 从 RunTimeStorage 查询已获取的酒水名称
    /// </summary>
    private static HashSet<string> GetUnlockedBeverages()
    {
        var result = new HashSet<string>();
        try
        {
            var beverages = GameData.RunTime.Common.RunTimeStorage.GetAllBeverages();
            if (beverages != null)
            {
                int totalCount = 0;
                int unresolvedCount = 0;
                var allIds = new List<string>();
                foreach (var bev in beverages)
                {
                    if (bev.Key == null) continue;
                    totalCount++;
                    int id = bev.Key.id;
                    var info = RecipeDatabase.GetBeverageById(id);
                    string name = info?.Name;
                    if (name == null) unresolvedCount++;
                    allIds.Add(id + (name != null ? "=" + name : "(未解析)"));
                    result.Add(name ?? id.ToString());
                }
                Instance?.Log.LogInfo($"[MystiaRec] 已解锁酒水: {result.Count}个");
                if (allIds.Count > 0)
                    Instance?.Log.LogInfo($"[MystiaRec] 酒水ID列表: {string.Join(",", allIds)}");
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] GetUnlockedBeverages: " + e.Message);
        }
        return result;
    }

    private static HashSet<string> GetAvailableCookers()
    {
        if (_cachedAvailableCookers != null)
            return _cachedAvailableCookers;

        var result = new HashSet<string>();
        try
        {
            var asm = typeof(NightScene.GuestManagementUtility.SpecialGuestsController).Assembly;
            var type = asm.GetTypes().FirstOrDefault(t => t.Name == "IzakayaConfigure");
            if (type != null)
            {
                var cookers = type?.GetProperty("CookerConfigure", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    as System.Collections.IEnumerable;
                if (cookers == null)
                    cookers = type?.GetField("_CookerConfigure_k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                        as System.Collections.IEnumerable;

                if (cookers != null)
                {
                    int cookerCount = 0;
                    foreach (var item in cookers)
                    {
                        cookerCount++;
                        // 处理 KeyValuePair<Cooker, Int32> 包装
                        object cooker = item;
                        if (item != null)
                        {
                            var itemType = item.GetType();
                            if (itemType.IsGenericType && itemType.GetGenericTypeDefinition().Name.StartsWith("KeyValuePair"))
                            {
                                var keyProp = itemType.GetProperty("Key");
                                if (keyProp != null)
                                    cooker = keyProp.GetValue(item);
                            }
                        }
                        string typeName = ReadMemberText(cooker, "Type", "type", "CookerType", "cookerType");
                        string seriesName = ReadMemberText(cooker, "Series", "series", "CookerSeries", "cookerSeries");
                        Instance?.Log.LogInfo($"[MystiaRec] 厨具[{cookerCount}]: CookerType={cooker?.GetType().Name}, Type={typeName}, Series={seriesName}, ToString={cooker}");
                        AddResolvedCooker(result, cooker);
                    }
                    Instance?.Log.LogInfo($"[MystiaRec] 厨具检测: CookerConfigure共{cookerCount}个, 解析结果=[{string.Join(",", result)}]");
                }
                else
                    Instance?.Log.LogWarning("[MystiaRec] 厨具检测: CookerConfigure为null");
            }
            else
                Instance?.Log.LogWarning("[MystiaRec] 厨具检测: 未找到IzakayaConfigure类型");

            if (result.Count == 0)
            {
                Instance?.Log.LogInfo("[MystiaRec] 厨具检测: 尝试RunTimeStorage备用路径...");
                AddAvailableCookersFromStorage(result);
                Instance?.Log.LogInfo($"[MystiaRec] 厨具检测: 备用路径结果=[{string.Join(",", result)}]");
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] GetAvailableCookers: " + e.Message);
        }
        _cachedAvailableCookers = result;
        return result;
    }

    private static void AddAvailableCookersFromStorage(HashSet<string> result)
    {
        try
        {
            var cookers = GameData.RunTime.Common.RunTimeStorage.GetAllCookers();
            if (cookers == null)
            {
                Instance?.Log.LogWarning("[MystiaRec] 厨具备用: GetAllCookers()返回null");
                return;
            }
            int i = 0;
            foreach (var item in cookers)
            {
                i++;
                // GetAllCookers 返回 KeyValuePair<Cooker, Int32>，需要提取 .Key
                object cooker = item;
                if (item != null)
                {
                    var itemType = item.GetType();
                    // 尝试作为 KeyValuePair 提取 .Key
                    if (itemType.IsGenericType && itemType.GetGenericTypeDefinition().Name.StartsWith("KeyValuePair"))
                    {
                        var keyProp = itemType.GetProperty("Key");
                        if (keyProp != null)
                            cooker = keyProp.GetValue(item);
                    }
                }

                string typeName = ReadMemberText(cooker, "Type", "type", "CookerType", "cookerType");
                string seriesName = ReadMemberText(cooker, "Series", "series", "CookerSeries", "cookerSeries");
                Instance?.Log.LogInfo($"[MystiaRec] 厨具备用[{i}]: CookerType={cooker?.GetType().Name}, Type={typeName}, Series={seriesName}, ToString={cooker}");
                AddResolvedCooker(result, cooker);
            }
            Instance?.Log.LogInfo($"[MystiaRec] 厨具备用: GetAllCookers共{i}个, 解析结果=[{string.Join(",", result)}]");
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] AddAvailableCookersFromStorage: " + e.Message);
        }
    }


    private static void AddResolvedCooker(HashSet<string> result, object cooker)
    {
        if (cooker == null) return;

        string typeName = ReadMemberText(cooker, "Type", "type", "CookerType", "cookerType");
        string seriesName = ReadMemberText(cooker, "Series", "series", "CookerSeries", "cookerSeries");
        string typeLower = typeName.ToLowerInvariant();
        string seriesLower = seriesName.ToLowerInvariant();
        bool isSparrow = seriesLower.Contains("sparrow");

        // 首次诊断：输出 Cooker 对象的所有可用属性/字段名
        // 解析厨具类型（中英文映射）
        string cookerType = null;
        if (typeLower.Contains("pot")) cookerType = "煮锅";
        else if (typeLower.Contains("grill")) cookerType = "烧烤架";
        else if (typeLower.Contains("fryer")) cookerType = "油锅";
        else if (typeLower.Contains("steamer")) cookerType = "蒸锅";
        else if (typeLower.Contains("cuttingboard")) cookerType = "料理台";

        if (cookerType != null)
        {
            if (isSparrow)
            {
                result.Add("夜雀" + cookerType); // 夜雀煮锅, 夜雀烧烤架, ...
                result.Add("夜雀厨具");           // 通用夜雀标记
            }
            result.Add(cookerType); // 夜雀煮锅也能当煮锅用
        }
    }

    private static string ReadMemberText(object obj, params string[] memberNames)
    {
        if (obj == null) return "";
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = obj.GetType();
        foreach (var memberName in memberNames)
        {
            var propValue = type.GetProperty(memberName, flags)?.GetValue(obj);
            if (propValue != null) return propValue.ToString();

            var fieldValue = type.GetField(memberName, flags)?.GetValue(obj);
            if (fieldValue != null) return fieldValue.ToString();
        }
        return "";
    }

    private static bool IsRecipeUnlocked(int recipeId, int foodId)
    {
        try
        {
            // 仅通过 HaveRecipe 判断——CheckRecipeIsLocked 对未相遇的食谱也可能返回 false
            if (GameData.RunTime.Common.RunTimeStorage.HaveRecipe(recipeId))
                return true;
            if (foodId != recipeId && GameData.RunTime.Common.RunTimeStorage.HaveRecipe(foodId))
                return true;
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] HaveRecipe check failed: " + e.Message);
        }

        return false;
    }

    /// <summary>
    /// 构建食材名 → 库存数量的字典
    /// </summary>
    private static Dictionary<string, int> GetIngredientStocks()
    {
        var result = new Dictionary<string, int>();
        try
        {
            int total = 0;
            foreach (var pair in RecipeDatabase.GetKnownIngredientIds())
            {
                if (string.IsNullOrEmpty(pair.Value)) continue;
                int count = GameData.RunTime.Common.RunTimeStorage.GetIngredientCountById(pair.Key);
                result[pair.Value] = count;
                total++;
            }
            if (total > 0)
            {
                var highStock = result.Where(kv => kv.Value >= 10).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}x{kv.Value}").ToList();
                Instance?.Log.LogInfo($"[MystiaRec] 食材库存: 登记{total}种, 库存≥10共{highStock.Count}种: {string.Join(", ", highStock)}");
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] GetIngredientStocks: " + e.Message);
        }
        return result;
    }

    private static PopularTrendState GetPopularTrend()
    {
        var result = new PopularTrendState();
        if (PluginConfig?.ConsiderPopularTrend?.Value == false)
            return result;

        try
        {
            AddResolvedFoodTags(result.LikeFoodTags, GameData.RunTime.Common.RunTimePlayerData.PopLikeFoodTags);
            AddResolvedFoodTags(result.HateFoodTags, GameData.RunTime.Common.RunTimePlayerData.PopHateFoodTags);
            AddResolvedBeverageTags(result.LikeBeverageTags, GameData.RunTime.Common.RunTimePlayerData.PopLikeBevTags);
            AddResolvedBeverageTags(result.HateBeverageTags, GameData.RunTime.Common.RunTimePlayerData.PopHateBevTags);
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] GetPopularTrend: " + e.Message);
        }

        return result;
    }

    private static void AddResolvedFoodTags(HashSet<string> target, System.Collections.IEnumerable tagIds)
    {
        AddResolvedTags(target, tagIds, true);
    }

    private static void AddResolvedFoodTags(HashSet<string> target, Il2CppSystem.Collections.Generic.List<int> tagIds)
    {
        AddResolvedTags(target, tagIds, true);
    }

    private static void AddResolvedBeverageTags(HashSet<string> target, System.Collections.IEnumerable tagIds)
    {
        AddResolvedTags(target, tagIds, false);
    }

    private static void AddResolvedBeverageTags(HashSet<string> target, Il2CppSystem.Collections.Generic.List<int> tagIds)
    {
        AddResolvedTags(target, tagIds, false);
    }

    private static void AddResolvedTags(HashSet<string> target, System.Collections.IEnumerable tagIds, bool food)
    {
        if (tagIds == null) return;

        foreach (var tagId in tagIds)
        {
            if (tagId == null) continue;
            try
            {
                int id = System.Convert.ToInt32(tagId);
                string tag = ResolveTagName(id, food);
                if (!string.IsNullOrEmpty(tag))
                    target.Add(tag);
            }
            catch { }
        }
    }

    private static void AddResolvedTags(HashSet<string> target, Il2CppSystem.Collections.Generic.List<int> tagIds, bool food)
    {
        if (tagIds == null) return;

        foreach (int id in tagIds)
        {
            string tag = ResolveTagName(id, food);
            if (!string.IsNullOrEmpty(tag))
                target.Add(tag);
        }
    }

    private static string ResolveTagName(int id, bool food)
    {
        try
        {
            var asm = typeof(NightScene.GuestManagementUtility.SpecialGuestsController).Assembly;
            var type = asm.GetTypes().FirstOrDefault(t => t.Name == "DataBaseLanguage");
            var methodName = food ? "GetFoodTag" : "GetBeverageTag";
            var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, new object[] { id })?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 从 RunTimeStorage 查询当前可用食材名称
    /// </summary>
    private static HashSet<string> GetAvailableIngredients()
    {
        var result = new HashSet<string>();
        try
        {
            // 仅从有库存数量 > 0 的食材中收集，避免推荐玩家库存不足的食材
            RegisterIngredientIdsFromCoreDatabase();
            AddKnownIngredientIdsFromLanguageDatabase();
            AddAvailableIngredientsFromAllIngredients(result);
            AddAvailableIngredientsFromStorageDictionary(result);
            AddAvailableIngredientsFromDatabase(result);
            AddAvailableIngredientsFromKnownIds(result);
            if (result.Count > 0)
                Instance?.Log.LogInfo("[MystiaRec] 当前食材: " + string.Join(",", result));
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] GetAvailableIngredients: " + e.Message);
        }
        return result;
    }

    private static void RegisterIngredientIdsFromCoreDatabase()
    {
        try
        {
            var asm = typeof(NightScene.GuestManagementUtility.SpecialGuestsController).Assembly;
            var coreType = asm.GetTypes().FirstOrDefault(t => t.Name == "DataBaseCore");
            var method = coreType?.GetMethod("GetAllIngredients", BindingFlags.Public | BindingFlags.Static);
            var ingredients = method?.Invoke(null, null) as System.Collections.IEnumerable;
            if (ingredients == null) return;

            int matched = 0;
            foreach (var ingredient in ingredients)
            {
                if (!TryReadObjectId(ingredient, out int id)) continue;
                string name = ResolveIngredientName(ingredient);
                if (string.IsNullOrEmpty(name))
                    name = ResolveIngredientNameFromLanguage(GetIngredientLanguage(GetDataBaseLanguageType(), id));
                if (!string.IsNullOrEmpty(name) && RecipeDatabase.RegisterIngredientId(id, name))
                    matched++;
            }

            if (matched > 0)
                Instance?.Log.LogInfo("[MystiaRec] 已从核心数据库登记食材ID: " + matched);
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] RegisterIngredientIdsFromCoreDatabase: " + e.Message);
        }
    }

    private static void AddKnownIngredientIdsFromLanguageDatabase()
    {
        try
        {
            var type = GetDataBaseLanguageType();
            var ingredients = GetStaticEnumerable(type, "Ingredients");
            if (ingredients == null) return;

            int matched = 0;
            foreach (var entry in ingredients)
            {
                var entryType = entry.GetType();
                var key = entryType.GetProperty("Key")?.GetValue(entry);
                if (key == null || !int.TryParse(key.ToString(), out int id)) continue;

                var value = entryType.GetProperty("Value")?.GetValue(entry);
                string name = ResolveIngredientNameFromLanguage(value);
                if (string.IsNullOrEmpty(name))
                    name = ResolveIngredientNameFromLanguage(GetIngredientLanguage(type, id));

                if (!string.IsNullOrEmpty(name) && RecipeDatabase.RegisterIngredientId(id, name))
                    matched++;
            }

            if (matched > 0)
                Instance?.Log.LogInfo("[MystiaRec] 已从语言表登记食材ID: " + matched);
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] AddKnownIngredientIdsFromLanguageDatabase: " + e.Message);
        }
    }

    private static void AddAvailableIngredientsFromAllIngredients(HashSet<string> result)
    {
        try
        {
            var ingredients = GameData.RunTime.Common.RunTimeStorage.GetAllIngredients();
            if (ingredients == null) return;

            foreach (var ingredient in ingredients)
            {
                if (ingredient == null) continue;
                if (!TryReadObjectId(ingredient, out int id)) continue;
                if (GameData.RunTime.Common.RunTimeStorage.GetIngredientCountById(id) <= 0) continue;

                string name = ResolveIngredientName(ingredient);
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] AddAvailableIngredientsFromAllIngredients: " + e.Message);
        }
    }

    private static void AddAvailableIngredientsFromStorageDictionary(HashSet<string> result)
    {
        try
        {
            var storageType = typeof(GameData.RunTime.Common.RunTimeStorage);
            var ingredients = storageType.GetProperty("Ingredients", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as System.Collections.IEnumerable;
            if (ingredients == null) return;

            int matched = 0;
            foreach (var entry in ingredients)
            {
                if (!TryReadDictionaryEntry(entry, out int id, out int count) || count <= 0)
                    continue;

                string name = RecipeDatabase.ResolveIngredientName(id);
                if (string.IsNullOrEmpty(name))
                {
                    name = ResolveIngredientNameFromLanguage(GetIngredientLanguage(GetDataBaseLanguageType(), id));
                }

                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(name);
                    matched++;
                }
            }

            if (matched > 0)
                Instance?.Log.LogInfo("[MystiaRec] 已从库存字典读取食材: " + matched);
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] AddAvailableIngredientsFromStorageDictionary: " + e.Message);
        }
    }

    private static bool TryReadDictionaryEntry(object entry, out int key, out int value)
    {
        key = 0;
        value = 0;
        if (entry == null) return false;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = entry.GetType();
        var keyObject = type.GetProperty("Key", flags)?.GetValue(entry) ?? type.GetField("key", flags)?.GetValue(entry);
        var valueObject = type.GetProperty("Value", flags)?.GetValue(entry) ?? type.GetField("value", flags)?.GetValue(entry);

        return keyObject != null &&
            valueObject != null &&
            int.TryParse(keyObject.ToString(), out key) &&
            int.TryParse(valueObject.ToString(), out value);
    }

    private static void AddAvailableIngredientsFromDatabase(HashSet<string> result)
    {
        try
        {
            var type = GetDataBaseLanguageType();
            var ingredients = GetStaticEnumerable(type, "Ingredients");
            if (ingredients == null) return;

            foreach (var entry in ingredients)
            {
                var entryType = entry.GetType();
                var key = entryType.GetProperty("Key")?.GetValue(entry);
                if (key == null || !int.TryParse(key.ToString(), out int id)) continue;
                if (GameData.RunTime.Common.RunTimeStorage.GetIngredientCountById(id) <= 0) continue;

                var value = entryType.GetProperty("Value")?.GetValue(entry);
                string name = ResolveIngredientNameFromLanguage(value);
                if (string.IsNullOrEmpty(name))
                    name = ResolveIngredientNameFromLanguage(GetIngredientLanguage(type, id));
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] AddAvailableIngredientsFromDatabase: " + e.Message);
        }
    }

    private static void AddAvailableIngredientsFromKnownIds(HashSet<string> result)
    {
        try
        {
            int matched = 0;
            int totalStockIds = 0;
            var unresolved = new List<string>();
            foreach (var pair in RecipeDatabase.GetKnownIngredientIds())
            {
                if (string.IsNullOrEmpty(pair.Value)) continue;

                int count = GameData.RunTime.Common.RunTimeStorage.GetIngredientCountById(pair.Key);
                if (count <= 0) continue;

                result.Add(pair.Value);
                matched++;
            }

            foreach (int id in GetLikelyIngredientIds())
            {
                if (!string.IsNullOrEmpty(RecipeDatabase.ResolveIngredientName(id))) continue;

                int count = GameData.RunTime.Common.RunTimeStorage.GetIngredientCountById(id);
                if (count <= 0) continue;

                totalStockIds++;
                string name = ResolveIngredientNameFromLanguage(GetIngredientLanguage(GetDataBaseLanguageType(), id));
                if (!string.IsNullOrEmpty(name) && RecipeDatabase.RegisterIngredientId(id, name))
                {
                    result.Add(name);
                    matched++;
                }
                else
                {
                    unresolved.Add($"{id}(x{count})");
                }
            }

            if (matched > 0)
                Instance?.Log.LogInfo("[MystiaRec] 已从已知食材ID读取库存: " + matched);
            Instance?.Log.LogInfo($"[MystiaRec] 食材库存: {result.Count}种");
            if (unresolved.Count > 0)
                Instance?.Log.LogInfo("[MystiaRec] 有库存但未解析名称的食材ID(库存): " + string.Join(", ", unresolved));
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] AddAvailableIngredientsFromKnownIds: " + e.Message);
        }
    }

    private static IEnumerable<int> GetLikelyIngredientIds()
    {
        for (int id = 1; id <= 600; id++)
            yield return id;
    }

    private static System.Type GetDataBaseLanguageType()
    {
        var asm = typeof(NightScene.GuestManagementUtility.SpecialGuestsController).Assembly;
        return asm.GetTypes().FirstOrDefault(t => t.Name == "DataBaseLanguage");
    }

    private static System.Collections.IEnumerable GetStaticEnumerable(System.Type type, string memberName)
    {
        if (type == null) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        return type.GetProperty(memberName, flags)?.GetValue(null) as System.Collections.IEnumerable
            ?? type.GetField(memberName, flags)?.GetValue(null) as System.Collections.IEnumerable
            ?? type.GetProperty($"_{memberName}_k__BackingField", flags)?.GetValue(null) as System.Collections.IEnumerable
            ?? type.GetField($"_{memberName}_k__BackingField", flags)?.GetValue(null) as System.Collections.IEnumerable;
    }

    private static object GetIngredientLanguage(System.Type dataBaseLanguageType, int id)
    {
        try
        {
            return dataBaseLanguageType
                ?.GetMethod("GetIngredientLang", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { id });
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadObjectId(object obj, out int id)
    {
        id = 0;
        if (obj == null) return false;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = obj.GetType();
        foreach (var memberName in new[] { "ID", "Id", "id", "IngredientID", "ingredientID" })
        {
            var propValue = type.GetProperty(memberName, flags)?.GetValue(obj);
            if (propValue != null && int.TryParse(propValue.ToString(), out id)) return true;

            var fieldValue = type.GetField(memberName, flags)?.GetValue(obj);
            if (fieldValue != null && int.TryParse(fieldValue.ToString(), out id)) return true;
        }

        return false;
    }

    private static string ResolveIngredientNameFromLanguage(object language)
    {
        if (language == null) return "";

        try
        {
            var type = language.GetType();
            foreach (var memberName in new[] { "Name", "name", "Title", "title", "Text", "text", "Value", "value", "Chinese", "chinese", "zh_CN", "zhCN" })
            {
                var prop = type.GetProperty(memberName);
                var value = prop?.GetValue(language)?.ToString();
                var resolved = RecipeDatabase.ResolveIngredientName(value);
                if (!string.IsNullOrEmpty(resolved)) return resolved;

                var field = type.GetField(memberName);
                value = field?.GetValue(language)?.ToString();
                resolved = RecipeDatabase.ResolveIngredientName(value);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }

            var text = language.ToString();
            var fromText = RecipeDatabase.ResolveIngredientName(text);
            if (!string.IsNullOrEmpty(fromText)) return fromText;
        }
        catch { }

        return "";
    }

    private static string ResolveIngredientName(object ingredient)
    {
        if (ingredient == null) return "";

        try
        {
            var type = ingredient.GetType();
            foreach (var memberName in new[] { "Name", "name", "IngredientName", "ingredientName" })
            {
                var prop = type.GetProperty(memberName);
                var value = prop?.GetValue(ingredient)?.ToString();
                if (!string.IsNullOrEmpty(value)) return RecipeDatabase.ResolveIngredientName(value);

                var field = type.GetField(memberName);
                value = field?.GetValue(ingredient)?.ToString();
                if (!string.IsNullOrEmpty(value)) return RecipeDatabase.ResolveIngredientName(value);
            }

            foreach (var memberName in new[] { "ID", "Id", "id", "IngredientID", "ingredientID" })
            {
                var prop = type.GetProperty(memberName);
                var value = prop?.GetValue(ingredient);
                if (value != null && int.TryParse(value.ToString(), out int id))
                {
                    var name = RecipeDatabase.ResolveIngredientName(id);
                    if (!string.IsNullOrEmpty(name)) return name;
                }

                var field = type.GetField(memberName);
                value = field?.GetValue(ingredient);
                if (value != null && int.TryParse(value.ToString(), out id))
                {
                    var name = RecipeDatabase.ResolveIngredientName(id);
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }

            var text = ingredient.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                var resolved = RecipeDatabase.ResolveIngredientName(text);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }
        }
        catch { }

        return "";
    }

}

/// <summary>
/// 单个稀客的推荐数据
/// </summary>
public class CustomerRecommendation
{
    public string CustomerName { get; set; }
    public int DeskCode { get; set; }
    public string ReqFoodTag { get; set; }
    public string ReqBevTag { get; set; }
    public List<Recommendation> Recommendations { get; set; } = new();
    public string StatusMessage { get; set; } = "";
    public float Timestamp { get; set; }
    public bool IsFadingOut { get; set; }
    public float FadeAlpha { get; set; } = 1f;

    // 拖拽位置（null=自动列布局）
    public float? DragX { get; set; }
    public float? DragY { get; set; }

    // 折叠状态（默认全部展开）
    public bool OverviewCollapsed { get; set; }
    public bool Rec1Collapsed { get; set; }
    public bool Rec2Collapsed { get; set; }
}

public class PopularTrendState
{
    public HashSet<string> LikeFoodTags { get; } = new();
    public HashSet<string> HateFoodTags { get; } = new();
    public HashSet<string> LikeBeverageTags { get; } = new();
    public HashSet<string> HateBeverageTags { get; } = new();

    public bool HasAny =>
        LikeFoodTags.Count > 0 ||
        HateFoodTags.Count > 0 ||
        LikeBeverageTags.Count > 0 ||
        HateBeverageTags.Count > 0;
}

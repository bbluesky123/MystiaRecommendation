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
    private static long _nextOrderSequence = 0;
    // 只有已经由实际料理锁定到左侧的稀客订单才预留酒水；右侧方案不占库存。
    private static readonly Dictionary<int, string> _beverageReservations = new();
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
        Harmony.PatchAll(typeof(Patches.CookerPatch));

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
    internal static void OnCustomerArrived(string customerName, string reqFoodTag, string reqBevTag, int deskCode,
        int orderBudget = -1, int fixedRecipeId = -1, UnityEngine.Vector3? customerWorldPosition = null,
        long orderKey = 0)
    {
        bool hasCustomer = DataEngine.HasCustomer(customerName);
        var customer = hasCustomer ? DataEngine.GetCustomer(customerName) : null;
        int maxBudget = GetEffectiveRemainingBudget(customerName, deskCode, customer, orderBudget);

        Instance?.Log.LogInfo($"[MystiaRec] 开始推荐: {customerName} 座位{deskCode} (已应用内部预算上限)");
        Instance?.Log.LogInfo($"[MystiaRec] 请求: 食物={reqFoodTag}, 酒水={reqBevTag}");
        if (fixedRecipeId >= 0)
            Instance?.Log.LogInfo($"[MystiaRec] 方案D: 固定料理foodId={fixedRecipeId}");
        if (!hasCustomer)
            Instance?.Log.LogWarning($"[MystiaRec] 稀客 [{customerName}] 不在数据表中，使用仅按订单匹配的推荐");

        // 每份完整订单都从 RunTimeStorage 捕获一次新快照，避免同场景内沿用过期状态。
        var gameState = CaptureGameState();
        var unlockedRecipes = gameState.UnlockedRecipes;
        var unlockedBeverages = gameState.OwnedBeverages;
        var availableCookers = gameState.OwnedCookers;
        var availableIngredients = gameState.AvailableIngredients;
        var ingredientStocks = gameState.IngredientStocks;
        var popularTrend = gameState.PopularTrend;

        Instance?.Log.LogInfo($"[MystiaRec] 已解锁: 料理={unlockedRecipes.Count}, 酒水={unlockedBeverages.Count}, 当前厨具={gameState.EquippedCookerCount}, 食材={availableIngredients.Count}");
        if (unlockedRecipes.Count > 0)
            Instance?.Log.LogInfo("[MystiaRec] 当前料理: " + string.Join(",", unlockedRecipes.Where(n => !int.TryParse(n, out _))));
        if (unlockedBeverages.Count > 0)
            Instance?.Log.LogInfo("[MystiaRec] 当前酒水: " + string.Join(",", unlockedBeverages.Where(n => !int.TryParse(n, out _))));
        if (availableCookers.Count > 0)
            Instance?.Log.LogInfo("[MystiaRec] 当前厨具能力: " + string.Join(",", availableCookers));
        if (popularTrend.HasAny)
            Instance?.Log.LogInfo($"[MystiaRec] 流行趋势: 食物喜爱={string.Join(",", popularTrend.LikeFoodTags)} 食物厌恶={string.Join(",", popularTrend.HateFoodTags)} 酒水喜爱={string.Join(",", popularTrend.LikeBeverageTags)} 酒水厌恶={string.Join(",", popularTrend.HateBeverageTags)}");

        var fixedRecipeInfo = fixedRecipeId >= 0
            ? RecipeDatabase.GetRecipeByFoodId(fixedRecipeId)
            : null;
        bool ownsFixedRecipe = fixedRecipeInfo != null && unlockedRecipes.Contains(fixedRecipeInfo.Name);
        var missingFixedRecipeIngredients = ownsFixedRecipe
            ? Matcher.GetMissingFixedRecipeIngredients(fixedRecipeId, availableIngredients, ingredientStocks)
            : new List<string>();
        bool cannotCookFixedRecipe = missingFixedRecipeIngredients.Count > 0;
        if (cannotCookFixedRecipe)
        {
            Instance?.Log.LogWarning(
                $"[MystiaRec] 方案D缺少基础食材 [{string.Join(",", missingFixedRecipeIngredients)}]，回退方案A/B/C");
        }

        // 计算推荐
        var recommendations = hasCustomer
            ? Matcher.CalculateByRequestTags(
                customerName, reqFoodTag, reqBevTag, maxBudget,
                unlockedRecipes, unlockedBeverages, availableIngredients, availableCookers, ingredientStocks, popularTrend,
                cannotCookFixedRecipe ? -1 : fixedRecipeId)
            : Matcher.CalculateUnknownByRequestTags(
                reqFoodTag, reqBevTag, maxBudget,
                unlockedRecipes, unlockedBeverages, availableIngredients, popularTrend);
        // 只保留前2个推荐
        if (recommendations.Count > 2)
            recommendations = recommendations.Take(2).ToList();

        string status = "";
        if (cannotCookFixedRecipe)
            status = "缺失料理，无法完成任务";
        else if (!hasCustomer)
            status = recommendations.Count == 0 ? "稀客数据未收录，无可用订单方案" : "稀客数据未收录，仅按订单匹配";
        else if (recommendations.Count == 0 && fixedRecipeId >= 0)
            status = "任务固定料理暂无可达到4分的方案";
        else if (recommendations.Count == 0)
            status = "预算不足，无可用方案";

        UpsertRecommendationCard(customerName, deskCode, reqFoodTag, reqBevTag, recommendations, status,
            orderBudget, fixedRecipeId, customerWorldPosition, orderKey);

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

    internal static void OnCustomerPending(string customerName, string reqFoodTag, string reqBevTag, int deskCode,
        string statusMessage, UnityEngine.Vector3? customerWorldPosition = null,
        PendingRecommendationState pendingState = PendingRecommendationState.NeedsInteraction)
    {
        if (string.IsNullOrEmpty(customerName)) return;

        // 防竞态：刚离场的座位不重新创建卡片
        if (IsDeskRecentlyDeparted(deskCode))
        {
            Instance?.Log.LogInfo($"[MystiaRec] 跳过离场冷却中的卡片: {customerName} 座位{deskCode}");
            return;
        }

        bool hasActiveOrderCard = ActiveRecommendations.Values.Any(cr =>
            cr.DeskCode == deskCode &&
            cr.CustomerName == customerName &&
            cr.PendingState == PendingRecommendationState.None &&
            cr.OrderKey > 0);
        if (hasActiveOrderCard) return;
        Instance?.Log.LogInfo($"[MystiaRec] 显示等待卡片: {customerName} 座位{deskCode} {statusMessage}");
        UpsertRecommendationCard(customerName, deskCode, reqFoodTag, reqBevTag,
            new List<Recommendation>(), statusMessage, customerWorldPosition: customerWorldPosition,
            pendingState: pendingState);
    }

    private static void UpsertRecommendationCard(
        string customerName,
        int deskCode,
        string reqFoodTag,
        string reqBevTag,
        List<Recommendation> recommendations,
        string statusMessage,
        int orderBudget = -1,
        int fixedRecipeId = -1,
        UnityEngine.Vector3? customerWorldPosition = null,
        long orderKey = 0,
        PendingRecommendationState pendingState = PendingRecommendationState.None)
    {
        // 同一稀客、同一座位采用原对象就地更新，保留拖拽位置、折叠状态和稳定卡片 ID。
        var existing = ActiveRecommendations.FirstOrDefault(kv =>
            kv.Value.DeskCode == deskCode && kv.Value.CustomerName == customerName);
        if (existing.Value != null)
        {
            var card = existing.Value;
            EnsureBudgetState(card, customerName, orderBudget);
            var previousPendingState = card.PendingState;
            bool isNewOrder = orderKey > 0 && card.OrderKey != orderKey;
            if (isNewOrder)
            {
                RuntimeOrderTracker.RemoveForCard(existing.Key);
                ReleaseBeverageReservation(existing.Key);
                card.OrderSequence = ++_nextOrderSequence;
                card.MatchedRecommendationIndex = -1;
                card.TrackingState = RecommendationTrackingState.AwaitingCook;
                card.ActiveAssignmentId = 0;
            }
            if (isNewOrder || previousPendingState != pendingState)
            {
                // 完整方案、桌边等待提示和桌边制作卡片使用不同布局区。
                // 跨状态时清除上一布局的拖拽坐标；同状态 F5 刷新仍保留位置。
                card.DragX = null;
                card.DragY = null;
            }
            card.ReqFoodTag = reqFoodTag;
            card.ReqBevTag = reqBevTag;
            card.OrderBudget = orderBudget;
            card.FixedRecipeId = fixedRecipeId;
            card.Recommendations = recommendations;
            card.StatusMessage = statusMessage;
            card.BeverageRefreshRequired = false;
            card.PendingState = pendingState;
            if (orderKey > 0)
                card.OrderKey = orderKey;
            card.Timestamp = UnityEngine.Time.time;
            card.IsFadingOut = false;
            card.FadeAlpha = 1f;
            if (customerWorldPosition.HasValue)
            {
                card.CustomerWorldPosition = customerWorldPosition.Value;
                card.HasCustomerWorldPosition = true;
            }
            return;
        }

        // 同一座位若残留其他客人的旧卡片，只清理旧客；新客创建新的稳定卡片。
        var toRemove = ActiveRecommendations
            .Where(kv => kv.Value.DeskCode == deskCode)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in toRemove)
        {
            RuntimeOrderTracker.RemoveForCard(key);
            ReleaseBeverageReservation(key);
            ActiveRecommendations.Remove(key);
        }

        // 存储到多稀客推荐字典（用唯一ID）
        int rid = GetNextRecommendId();
        var newCard = new CustomerRecommendation
        {
            CustomerName = customerName,
            DeskCode = deskCode,
            ReqFoodTag = reqFoodTag,
            ReqBevTag = reqBevTag,
            OrderBudget = orderBudget,
            FixedRecipeId = fixedRecipeId,
            Recommendations = recommendations,
            StatusMessage = statusMessage,
            PendingState = pendingState,
            Timestamp = UnityEngine.Time.time,
            OrderKey = orderKey,
            OrderSequence = ++_nextOrderSequence,
            TrackingState = RecommendationTrackingState.AwaitingCook
        };
        EnsureBudgetState(newCard, customerName, orderBudget);
        ActiveRecommendations[rid] = newCard;
        if (customerWorldPosition.HasValue)
        {
            ActiveRecommendations[rid].CustomerWorldPosition = customerWorldPosition.Value;
            ActiveRecommendations[rid].HasCustomerWorldPosition = true;
        }
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
            RuntimeOrderTracker.RemoveForCard(key);
            ReleaseBeverageReservation(key);
            ActiveRecommendations.Remove(key);
            Instance?.Log.LogInfo($"[MystiaRec] 稀客离店: 座位{deskCode}");
        }

        // 防止离场后 OnGetGuestName 钩子重新创建卡片
        _recentlyDepartedDesks.Add(deskCode);
        _lastDepartTime = UnityEngine.Time.time;
    }

    /// <summary>
    /// 游戏确认本轮料理和酒水都已上齐后，扣除本轮实际账面价并关闭当前卡片。
    /// 不校验玩家最终交付的内容是否等于推荐方案。
    /// </summary>
    internal static void OnOrderFulfilled(int deskCode, object servedFood = null, object servedBeverage = null)
    {
        if (deskCode < 0) return;
        var keys = ActiveRecommendations
            .Where(kv => kv.Value.DeskCode == deskCode)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in keys)
        {
            RuntimeOrderTracker.RemoveForCard(key);
            ReleaseBeverageReservation(key);
            var card = ActiveRecommendations[key];
            int acceptedOrderPrice = ResolveAcceptedOrderPrice(card, servedFood, servedBeverage);
            if (acceptedOrderPrice > 0)
            {
                card.RemainingBudget = System.Math.Max(0, card.RemainingBudget - acceptedOrderPrice);
                card.LastAcceptedOrderPrice = acceptedOrderPrice;
                card.AcceptedOrderCount++;
                Instance?.Log?.LogInfo(
                    $"[MystiaRec] 已按本轮订单更新内部预算状态: {card.CustomerName} 座位{deskCode + 1}");
            }
            else
            {
                Instance?.Log?.LogWarning(
                    $"[MystiaRec] 无法识别已接受订单价格，预算未扣减: {card.CustomerName} 座位{deskCode + 1}");
            }
            card.ReqFoodTag = "";
            card.ReqBevTag = "";
            card.OrderBudget = -1;
            card.FixedRecipeId = -1;
            card.Recommendations = new List<Recommendation>();
            card.StatusMessage = "等待下一轮";
            card.PendingState = PendingRecommendationState.WaitingNextRound;
            card.MatchedRecommendationIndex = -1;
            card.TrackingState = RecommendationTrackingState.AwaitingCook;
            card.ActiveAssignmentId = 0;
            card.BeverageRefreshRequired = false;
            card.DragX = null;
            card.DragY = null;
            card.Timestamp = UnityEngine.Time.time;
            card.IsFadingOut = false;
            card.FadeAlpha = 1f;
        }
        if (keys.Count > 0)
            Instance?.Log?.LogInfo($"[MystiaRec] 本轮料理和酒水均已上齐，进入等待下一轮: 座位{deskCode + 1}");
    }

    private static int GetEffectiveRemainingBudget(
        string customerName,
        int deskCode,
        CustomerData customer,
        int orderBudget)
    {
        // 游戏运行时余额已经包含符卡、店铺状态、退款和免费订单等动态效果，
        // 一旦读取成功就应覆盖静态区间与插件自己的递减估算。
        if (orderBudget >= 0)
            return orderBudget;

        var card = ActiveRecommendations.Values.FirstOrDefault(value =>
            value != null && value.DeskCode == deskCode && value.CustomerName == customerName);
        int fallback = card?.BudgetInitialized == true
            ? card.RemainingBudget
            : (customer?.BudgetUpperBound ?? 999);
        return System.Math.Max(0, fallback);
    }

    private static void EnsureBudgetState(CustomerRecommendation card, string customerName, int orderBudget)
    {
        if (card == null) return;
        if (!card.BudgetInitialized)
        {
            var customer = DataEngine?.GetCustomer(customerName);
            int upperBound = customer?.BudgetUpperBound ?? (orderBudget >= 0 ? orderBudget : 999);
            card.BudgetUpperBound = System.Math.Max(0, upperBound);
            card.RemainingBudget = card.BudgetUpperBound;
            card.BudgetInitialized = true;
        }

        // 每个新订单都以游戏真实余额重新同步；该值只存于内部状态，不参与 UI。
        if (orderBudget >= 0)
            card.RemainingBudget = orderBudget;
    }

    private static int ResolveAcceptedOrderPrice(
        CustomerRecommendation card,
        object servedFood,
        object servedBeverage)
    {
        int foodId = TryReadServedItemId(servedFood, "Id", "ID", "FoodId", "FoodID");
        int beverageId = TryReadServedItemId(servedBeverage,
            "Id", "ID", "BeverageId", "BeverageID", "ItemId", "ItemID", "Value");
        var recipe = foodId >= 0 ? RecipeDatabase.GetRecipeByFoodId(foodId) : null;
        var beverage = beverageId >= 0 ? RecipeDatabase.GetBeverageById(beverageId) : null;

        if (recipe != null && beverage != null)
            return recipe.Price + beverage.Price;

        Recommendation matched = null;
        if (card != null
            && card.MatchedRecommendationIndex >= 0
            && card.MatchedRecommendationIndex < card.Recommendations.Count)
            matched = card.Recommendations[card.MatchedRecommendationIndex];

        if (matched != null)
        {
            int foodPrice = recipe?.Price ?? RecipeDatabase.GetRecipe(matched.RecipeName)?.Price ?? 0;
            int beveragePrice = beverage?.Price ?? RecipeDatabase.GetBeverage(matched.BeverageName)?.Price ?? 0;
            if (foodPrice > 0 || beveragePrice > 0)
                return foodPrice + beveragePrice;
            if (matched.TotalPrice > 0)
                return matched.TotalPrice;
        }

        // 玩家未制作已显示的方案、且游戏对象没有暴露完整 ID 时，按方案 A 的最高价
        // 保守扣减，确保后续推荐累计价格不会越过静态预算上界。
        return card?.Recommendations
            ?.Where(recommendation => recommendation != null)
            .Select(recommendation => recommendation.TotalPrice)
            .DefaultIfEmpty(0)
            .Max() ?? 0;
    }

    private static int TryReadServedItemId(object value, params string[] memberNames)
    {
        if (value == null) return -1;
        try
        {
            if (value is int directId) return directId;
            if (int.TryParse(value.ToString(), out int parsedDirect) && parsedDirect >= 0)
                return parsedDirect;

            var type = value.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (string memberName in memberNames)
            {
                object memberValue = type.GetProperty(memberName, flags)?.GetValue(value)
                    ?? type.GetField(memberName, flags)?.GetValue(value);
                if (memberValue != null
                    && int.TryParse(memberValue.ToString(), out int id)
                    && id >= 0)
                    return id;
            }
        }
        catch { }
        return -1;
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
            RuntimeOrderTracker.RemoveForCard(key);
            ReleaseBeverageReservation(key);
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
        RuntimeOrderTracker.Reset();
        _beverageReservations.Clear();
        _nextOrderSequence = 0;
        _cachedUnlockedRecipes = null;
        _cachedAvailableCookers = null;
        _cachedEquippedCookerCount = 0;
    }

    private static HashSet<string> _cachedUnlockedRecipes;
    private static HashSet<string> _cachedAvailableCookers;
    private static int _cachedEquippedCookerCount;

    /// <summary>
    /// 手动刷新所有活跃卡片的推荐（快捷键触发）。
    /// 重新读取游戏状态并重新计算推荐，适用于稀客长时间等待后仍需查看推荐的场景。
    /// </summary>
    internal static void RefreshActiveRecommendations()
    {
        _cachedUnlockedRecipes = null;
        GetUnlockedRecipes();

        var cards = ActiveRecommendations.ToList();
        foreach (var kv in cards)
        {
            var card = kv.Value;
            if (card.TrackingState != RecommendationTrackingState.AwaitingCook)
                continue;
            // 料理和酒水需求必须都已读到。只拿到半边需求时仍属于订单读取状态，
            // F5 不能用残缺条件强行生成方案。
            if (string.IsNullOrEmpty(card.ReqFoodTag) || string.IsNullOrEmpty(card.ReqBevTag))
                continue;

            Instance?.Log?.LogInfo($"[MystiaRec] 刷新推荐: {card.CustomerName} 座位{card.DeskCode}");
            OnCustomerArrived(card.CustomerName, card.ReqFoodTag ?? "", card.ReqBevTag ?? "",
                card.DeskCode, card.OrderBudget, card.FixedRecipeId,
                card.HasCustomerWorldPosition ? card.CustomerWorldPosition : null,
                card.OrderKey);
        }

        if (cards.Count == 0)
            Instance?.Log?.LogInfo("[MystiaRec] 刷新推荐: 无活跃卡片");
    }

    /// <summary>
    /// 实际料理开始制作时锁定一瓶酒水。原酒水无库存时只平替酒水；平替失败则保持右侧卡片，等待 F5。
    /// </summary>
    internal static bool TryLockBeverageForCook(
        int cardId,
        CustomerRecommendation card,
        IReadOnlyList<int> recommendationIndexes,
        out int selectedIndex)
    {
        selectedIndex = -1;
        if (card == null || recommendationIndexes == null) return false;

        var indexes = recommendationIndexes
            .Where(index => index >= 0 && index < card.Recommendations.Count)
            .Distinct()
            .ToList();
        if (indexes.Count == 0) return false;

        ReleaseBeverageReservation(cardId);
        var allocatable = GetAllocatableBeverageStocks();

        // A/B 料理完全相同时，优先沿用显示顺序中仍有库存的原酒水。
        foreach (int index in indexes)
        {
            string beverage = card.Recommendations[index]?.BeverageName;
            if (string.IsNullOrEmpty(beverage)
                || !allocatable.TryGetValue(beverage, out int count)
                || count <= 0)
                continue;

            ReserveBeverage(cardId, beverage);
            selectedIndex = index;
            ClearBeverageRefreshWarning(card);
            MarkUnavailableDecisionCards(cardId);
            return true;
        }

        // 原酒水已被前序左侧订单占满：保持料理/食材/厨具不变，只更换酒水。
        var availableNames = allocatable
            .Where(pair => pair.Value > 0)
            .Select(pair => pair.Key)
            .ToHashSet();
        var customer = DataEngine.GetCustomer(card.CustomerName);
        int maxBudget = GetEffectiveRemainingBudget(
            card.CustomerName, card.DeskCode, customer, card.OrderBudget);
        var popularTrend = GetPopularTrend();

        foreach (int index in indexes)
        {
            var source = card.Recommendations[index];
            var replacement = Matcher.FindBeverageReplacement(
                source, card.CustomerName, card.ReqFoodTag, card.ReqBevTag,
                maxBudget, availableNames, popularTrend);
            if (replacement == null) continue;

            card.Recommendations[index] = replacement;
            ReserveBeverage(cardId, replacement.BeverageName);
            selectedIndex = index;
            ClearBeverageRefreshWarning(card);
            MarkUnavailableDecisionCards(cardId);
            Instance?.Log?.LogInfo(
                $"[MystiaRec] 酒水库存不足，自动平替: 座位{card.DeskCode + 1} " +
                $"{source.BeverageName} -> {replacement.BeverageName}");
            return true;
        }

        card.MatchedRecommendationIndex = -1;
        card.TrackingState = RecommendationTrackingState.AwaitingCook;
        card.ActiveAssignmentId = 0;
        card.DragX = null;
        card.DragY = null;
        card.StatusMessage = "方案酒水库存不足，请按 F5 刷新方案";
        card.BeverageRefreshRequired = true;
        Instance?.Log?.LogInfo(
            $"[MystiaRec] 酒水无法平替，保留右侧等待 F5: 座位{card.DeskCode + 1} {card.CustomerName}");
        return false;
    }

    internal static void ReleaseBeverageReservation(int cardId)
    {
        if (_beverageReservations.Remove(cardId, out string beverage))
            Instance?.Log?.LogInfo($"[MystiaRec] 释放酒水预留: card={cardId} {beverage}");
    }

    internal static void ReleaseBeverageReservationForDesk(int deskCode)
    {
        var cardIds = ActiveRecommendations
            .Where(pair => pair.Value.DeskCode == deskCode)
            .Select(pair => pair.Key)
            .ToList();
        foreach (int cardId in cardIds)
            ReleaseBeverageReservation(cardId);
    }

    private static void ReserveBeverage(int cardId, string beverage)
    {
        if (string.IsNullOrEmpty(beverage)) return;
        _beverageReservations[cardId] = beverage;
        Instance?.Log?.LogInfo($"[MystiaRec] 锁定酒水预留: card={cardId} {beverage}");
    }

    private static Dictionary<string, int> GetReservedBeverageCounts(int excludeCardId = -1)
    {
        return _beverageReservations
            .Where(pair => pair.Key != excludeCardId && !string.IsNullOrEmpty(pair.Value))
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    private static Dictionary<string, int> GetAllocatableBeverageStocks(int excludeCardId = -1)
    {
        var stocks = GetBeverageStocks();
        var reserved = GetReservedBeverageCounts(excludeCardId);
        foreach (var pair in reserved)
        {
            if (stocks.ContainsKey(pair.Key))
                stocks[pair.Key] = System.Math.Max(0, stocks[pair.Key] - pair.Value);
        }
        return stocks;
    }

    private static void MarkUnavailableDecisionCards(int excludeCardId)
    {
        var allocatable = GetAllocatableBeverageStocks();
        foreach (var pair in ActiveRecommendations)
        {
            if (pair.Key == excludeCardId) continue;
            var card = pair.Value;
            if (card == null
                || card.PendingState != PendingRecommendationState.None
                || card.TrackingState != RecommendationTrackingState.AwaitingCook
                || card.Recommendations.Count == 0)
                continue;

            bool hasUnavailableRecommendation = card.Recommendations.Any(rec =>
                rec != null
                && !string.IsNullOrEmpty(rec.BeverageName)
                && (!allocatable.TryGetValue(rec.BeverageName, out int count) || count <= 0));
            if (!hasUnavailableRecommendation) continue;

            card.StatusMessage = "推荐酒水已被其他订单预留，请按 F5 刷新方案";
            card.BeverageRefreshRequired = true;
            Instance?.Log?.LogInfo(
                $"[MystiaRec] 右侧方案酒水已无可分配库存，等待 F5: 座位{card.DeskCode + 1} {card.CustomerName}");
        }
    }

    private static void ClearBeverageRefreshWarning(CustomerRecommendation card)
    {
        if (card == null) return;
        if (card.BeverageRefreshRequired)
            card.StatusMessage = "";
        card.BeverageRefreshRequired = false;
    }

    /// <summary>
    /// 直接从 RunTimeStorage 读取当前存档实际拥有的料理。
    /// 接口不可用时返回空集合并记录错误，不再根据日期、等级或羁绊推断。
    /// </summary>
    private static HashSet<string> GetUnlockedRecipes(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedUnlockedRecipes != null)
            return _cachedUnlockedRecipes;

        var result = new HashSet<string>();
        var mappings = new List<string>();
        try
        {
            var recipes = GameData.RunTime.Common.RunTimeStorage.GetAllRecipes();
            if (recipes == null)
            {
                Instance?.Log?.LogWarning(
                    "[MystiaRec] RunTimeStorage.GetAllRecipes 返回 null，本次不生成料理推荐");
            }
            else
            {
                foreach (var entry in recipes)
                {
                    if (entry == null) continue;
                    int foodId = entry.foodID;
                    var info = RecipeDatabase.GetRecipeByFoodId(foodId);
                    if (info == null)
                    {
                        mappings.Add($"runtimeRecipeId={entry.id}->foodId={foodId}(未收录)");
                        continue;
                    }

                    result.Add(info.Name);
                    mappings.Add($"runtimeRecipeId={entry.id}->foodId={foodId}({info.Name})");
                }
            }

            Instance?.Log?.LogInfo(
                $"[MystiaRec] 从RunTimeStorage读取料理所有权: {result.Count}个Food ID, " +
                $"映射=[{string.Join("; ", mappings)}]");
        }
        catch (System.Exception e)
        {
            Instance?.Log?.LogWarning(
                "[MystiaRec] 读取料理所有权失败，本次不生成料理推荐: " + e.Message);
        }

        _cachedUnlockedRecipes = result;
        return result;
    }


    /// <summary>
    /// 从 RunTimeStorage 查询酒水的实际持有数量。
    /// </summary>
    private static Dictionary<string, int> GetBeverageStocks()
    {
        var result = new Dictionary<string, int>();
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
                    if (bev.Key == null || bev.Value <= 0) continue;
                    totalCount++;
                    int id = bev.Key.id;
                    var info = RecipeDatabase.GetBeverageById(id);
                    string name = info?.Name;
                    if (name == null) unresolvedCount++;
                    allIds.Add(id + (name != null ? "=" + name : "(未解析)"));
                    string key = name ?? id.ToString();
                    result[key] = result.TryGetValue(key, out int current)
                        ? current + bev.Value
                        : bev.Value;
                }
                Instance?.Log.LogInfo($"[MystiaRec] 当前酒水库存: {result.Count}种");
                if (allIds.Count > 0)
                    Instance?.Log.LogInfo($"[MystiaRec] 酒水ID列表: {string.Join(",", allIds)}");
            }
        }
        catch (System.Exception e)
        {
            Instance?.Log.LogWarning("[MystiaRec] GetBeverageStocks: " + e.Message);
        }
        return result;
    }

    private static HashSet<string> GetUnlockedBeverages(Dictionary<string, int> beverageStocks = null)
    {
        beverageStocks ??= GetBeverageStocks();
        return beverageStocks
            .Where(pair => pair.Value > 0)
            .Select(pair => pair.Key)
            .ToHashSet();
    }

    private static HashSet<string> GetAvailableCookers(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedAvailableCookers != null)
            return _cachedAvailableCookers;

        var result = new HashSet<string>();
        bool liveConfigurationAvailable = false;
        try
        {
            // 营业场景中的 CookSystemManager 只包含本轮实际放置的厨具。
            // RunTimeStorage.GetAllCookers() 是永久仓库，不能作为营业主路径。
            if (NightScene.CookingUtility.CookSystemManager.hasInstance)
            {
                var manager = NightScene.CookingUtility.CookSystemManager.Instance;
                var placedCookers = manager?._AllCookers_k__BackingField;
                if (placedCookers != null)
                {
                    liveConfigurationAvailable = true;
                    int slotCount = 0;
                    int equippedCookerCount = 0;
                    var enumerator = placedCookers.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        slotCount++;
                        var controller = enumerator.Current.Value;
                        if (controller == null || controller.Cooker == null) continue;
                        object cooker = controller.Cooker;
                        string typeName = ReadMemberText(cooker, "Type", "type", "CookerType", "cookerType");
                        string seriesName = ReadMemberText(cooker, "Series", "series", "CookerSeries", "cookerSeries");
                        // 场景字典会为未装备的槽位放入 Type=Empty 的占位 Cooker；不能计为携带厨具。
                        if (string.Equals(typeName, "Empty", System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        equippedCookerCount++;
                        Instance?.Log.LogInfo($"[MystiaRec] 营业厨具[{equippedCookerCount}]: Type={typeName}, Series={seriesName}");
                        AddResolvedCooker(result, cooker);
                    }
                    _cachedEquippedCookerCount = equippedCookerCount;
                    Instance?.Log.LogInfo($"[MystiaRec] 营业厨具检测: 实际携带={equippedCookerCount}个/总槽位={slotCount}, " +
                        $"可用能力=[{string.Join(",", result)}]");
                }
            }

            if (!liveConfigurationAvailable)
            {
                Instance?.Log.LogWarning("[MystiaRec] 营业厨具管理器不可用，使用永久仓库兼容兜底；该结果仅供非营业场景诊断");
                AddAvailableCookersFromStorage(result);
                _cachedEquippedCookerCount = 0;
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
                        var value = itemType.GetProperty("Value")?.GetValue(item);
                        if (value != null && int.TryParse(value.ToString(), out int count) && count <= 0)
                            continue;
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

    /// <summary>
    /// 在 Unity 主线程捕获本次推荐使用的完整运行时状态。
    /// JSON 只提供稳定定义与 ID 映射；是否拥有、当前库存和营业配置均以存档为准。
    /// </summary>
    private static RecommendationGameStateSnapshot CaptureGameState()
    {
        // 先发现并注册运行时食材 ID，再读取库存，避免遗漏高位 DLC ID。
        var availableIngredients = GetAvailableIngredients();
        var ingredientStocks = GetIngredientStocks();
        var beverageStocks = GetAllocatableBeverageStocks();
        var snapshot = new RecommendationGameStateSnapshot
        {
            UnlockedRecipes = new HashSet<string>(GetUnlockedRecipes(forceRefresh: true)),
            OwnedBeverages = new HashSet<string>(GetUnlockedBeverages(beverageStocks)),
            BeverageStocks = new Dictionary<string, int>(beverageStocks),
            OwnedCookers = new HashSet<string>(GetAvailableCookers(forceRefresh: true)),
            EquippedCookerCount = _cachedEquippedCookerCount,
            AvailableIngredients = new HashSet<string>(availableIngredients),
            IngredientStocks = new Dictionary<string, int>(ingredientStocks),
            PopularTrend = GetPopularTrend()
        };

        Instance?.Log?.LogInfo(
            $"[MystiaRec] 运行时快照: 料理={snapshot.UnlockedRecipes.Count}, " +
            $"可分配酒水={snapshot.OwnedBeverages.Count}, " +
            $"可用食材={snapshot.AvailableIngredients.Count}, 营业厨具={snapshot.EquippedCookerCount}, " +
            $"厨具能力={snapshot.OwnedCookers.Count}");
        return snapshot;
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
internal enum PendingRecommendationState
{
    None,
    NeedsInteraction,
    ReadingOrder,
    WaitingNextRound
}

public class CustomerRecommendation
{
    public string CustomerName { get; set; }
    public int DeskCode { get; set; }
    public string ReqFoodTag { get; set; }
    public string ReqBevTag { get; set; }
    public int OrderBudget { get; set; } = -1;
    /// <summary>夜雀小助手 price 区间上界；在本次入座期间保持不变。</summary>
    public int BudgetUpperBound { get; set; } = 999;
    /// <summary>每轮订单被接受后按料理与酒水账面价递减。</summary>
    public int RemainingBudget { get; set; } = 999;
    public bool BudgetInitialized { get; set; }
    public int LastAcceptedOrderPrice { get; set; }
    public int AcceptedOrderCount { get; set; }
    public int FixedRecipeId { get; set; } = -1;
    public List<Recommendation> Recommendations { get; set; } = new();
    public string StatusMessage { get; set; } = "";
    public bool BeverageRefreshRequired { get; set; }
    internal PendingRecommendationState PendingState { get; set; }
    public long OrderKey { get; set; }
    public long OrderSequence { get; set; }
    // -1=尚未匹配；-2=A/B料理完全相同，仅酒水无法由厨具判断；>=0=已自动匹配的方案索引。
    public int MatchedRecommendationIndex { get; set; } = -1;
    internal RecommendationTrackingState TrackingState { get; set; } = RecommendationTrackingState.AwaitingCook;
    public long ActiveAssignmentId { get; set; }
    public float Timestamp { get; set; }
    public bool IsFadingOut { get; set; }
    public float FadeAlpha { get; set; } = 1f;

    // 当前显示状态下的手动拖拽位置（null=由决策区/桌边区自动布局）
    public float? DragX { get; set; }
    public float? DragY { get; set; }
    public UnityEngine.Vector3 CustomerWorldPosition { get; set; }
    public bool HasCustomerWorldPosition { get; set; }

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

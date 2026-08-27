using System.Collections.Generic;

namespace MystiaRecommendation.Engine;

/// <summary>
/// 游戏状态缓存
/// 通过 Harmony Patches 实时更新游戏内部状态
/// </summary>
public class GameStateCache
{
    /// <summary>
    /// 当前稀客名称
    /// </summary>
    public string CurrentCustomerName { get; set; }

    /// <summary>
    /// 稀客当前位置（世界坐标）
    /// </summary>
    public UnityEngine.Vector3 CustomerPosition { get; set; }

    /// <summary>
    /// 当前已解锁的食谱名称列表
    /// </summary>
    public HashSet<string> UnlockedRecipes { get; set; } = new();

    /// <summary>
    /// 当前持有的食材 {食材名: 数量}
    /// </summary>
    public Dictionary<string, int> Ingredients { get; set; } = new();

    /// <summary>
    /// 当前可用的厨具能力名称列表
    /// </summary>
    public HashSet<string> OwnedCookers { get; set; } = new();

    /// <summary>
    /// 已获取的酒水名称列表
    /// </summary>
    public HashSet<string> OwnedBeverages { get; set; } = new();

    /// <summary>
    /// 当前流行趋势标签（可为 null）
    /// </summary>
    public string PopularTrendTag { get; set; }

    /// <summary>
    /// 当前流行趋势方向（true = 负面/厌恶方向, false = 正面/喜爱方向）
    /// </summary>
    public bool PopularTrendIsNegative { get; set; }

    /// <summary>
    /// 明星店效果是否激活
    /// </summary>
    public bool IsFamousShopActive { get; set; }

    /// <summary>
    /// 当前是否使用夜雀厨具
    /// </summary>
    public bool HasMystiaCooker { get; set; }

    /// <summary>
    /// 获取当前游戏状态快照
    /// </summary>
    public GameStateSnapshot GetCurrentState()
    {
        return new GameStateSnapshot
        {
            CustomerName = CurrentCustomerName,
            CustomerPosition = CustomerPosition,
            UnlockedRecipes = new HashSet<string>(UnlockedRecipes),
            Ingredients = new Dictionary<string, int>(Ingredients),
            OwnedCookers = new HashSet<string>(OwnedCookers),
            OwnedBeverages = new HashSet<string>(OwnedBeverages),
            PopularTrendTag = PopularTrendTag,
            PopularTrendIsNegative = PopularTrendIsNegative,
            IsFamousShopActive = IsFamousShopActive,
            HasMystiaCooker = HasMystiaCooker
        };
    }
}

/// <summary>
/// 游戏状态快照（不可变副本）
/// </summary>
public class GameStateSnapshot
{
    public string CustomerName { get; init; }
    public UnityEngine.Vector3 CustomerPosition { get; init; }
    public HashSet<string> UnlockedRecipes { get; init; }
    public Dictionary<string, int> Ingredients { get; init; }
    public HashSet<string> OwnedCookers { get; init; }
    public HashSet<string> OwnedBeverages { get; init; }
    public string PopularTrendTag { get; init; }
    public bool PopularTrendIsNegative { get; init; }
    public bool IsFamousShopActive { get; init; }
    public bool HasMystiaCooker { get; init; }
}

/// <summary>
/// 一次推荐计算使用的完整运行时快照。
/// 在 Unity 主线程捕获后，只包含普通托管集合，推荐引擎不需要继续访问 IL2CPP 对象。
/// </summary>
public sealed class RecommendationGameStateSnapshot
{
    public HashSet<string> UnlockedRecipes { get; init; } = new();
    public HashSet<string> OwnedBeverages { get; init; } = new();
    /// <summary>扣除已锁定稀客订单预留量后的可分配酒水库存。</summary>
    public Dictionary<string, int> BeverageStocks { get; init; } = new();
    /// <summary>由本次营业厨具解析出的可用类型与夜雀能力标记。</summary>
    public HashSet<string> OwnedCookers { get; init; } = new();
    /// <summary>本次营业场景中非空、实际携带的厨具数量；不同于总槽位和算法能力名称数量。</summary>
    public int EquippedCookerCount { get; init; }
    public HashSet<string> AvailableIngredients { get; init; } = new();
    public Dictionary<string, int> IngredientStocks { get; init; } = new();
    public PopularTrendState PopularTrend { get; init; } = new();
}

namespace MystiaRecommendation.Engine;

/// <summary>
/// 当前订单的预算约束。运行时余额已经包含店铺、装饰和符卡造成的数值变化；
/// EnduranceLimit 只负责把稀客本身允许的小幅超预算加入本单价格上限。
/// </summary>
public sealed class OrderBudgetContext
{
    public int RemainingFund { get; init; } = -1;
    public float EnduranceLimit { get; init; } = 1f;
    public bool HasRuntimeBudget { get; init; }
    public bool IsFreeOrder { get; init; }

    public bool HasKnownBudget => RemainingFund >= 0;
    public bool BypassPriceLimit => IsFreeOrder;

    public int EffectiveLimit
    {
        get
        {
            if (BypassPriceLimit)
                return int.MaxValue;

            int remaining = System.Math.Max(0, RemainingFund);
            double endurance = System.Math.Max(1d, EnduranceLimit);
            double limit = System.Math.Floor(remaining * endurance);
            return limit >= int.MaxValue ? int.MaxValue : (int)limit;
        }
    }

    public bool Allows(int totalPrice)
    {
        if (totalPrice < 0) return false;
        return BypassPriceLimit || totalPrice <= EffectiveLimit;
    }

    public static bool ResolveFreeOrder(
        bool guestCurrentOrderFree,
        bool orderPropertyFree,
        bool orderObjectFree)
    {
        return guestCurrentOrderFree || orderPropertyFree || orderObjectFree;
    }

    /// <summary>
    /// 仅用于运行时暂时不可读时的保守回退。免费订单保持余额不变；
    /// 退款和其他结算修正会在游戏更新 GetFund 后覆盖这个估算。
    /// </summary>
    public int EstimateRemainingAfterOrder(int acceptedOrderPrice)
    {
        if (IsFreeOrder || acceptedOrderPrice <= 0)
            return System.Math.Max(0, RemainingFund);
        return System.Math.Max(0, RemainingFund - acceptedOrderPrice);
    }

    public OrderBudgetContext WithRuntimeRemaining(int remainingFund)
    {
        return new OrderBudgetContext
        {
            RemainingFund = System.Math.Max(0, remainingFund),
            EnduranceLimit = EnduranceLimit,
            HasRuntimeBudget = true,
            IsFreeOrder = IsFreeOrder
        };
    }

    public OrderBudgetContext AsFallback(int remainingFund, float enduranceLimit)
    {
        return new OrderBudgetContext
        {
            RemainingFund = System.Math.Max(0, remainingFund),
            EnduranceLimit = enduranceLimit > 0 ? enduranceLimit : 1f,
            HasRuntimeBudget = false,
            IsFreeOrder = IsFreeOrder
        };
    }
}

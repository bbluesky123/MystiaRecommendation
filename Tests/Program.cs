using MystiaRecommendation.Engine;
using System.Text.Json;

var tests = new List<(string Name, Action Run)>
{
    ("耐受系数边界包含", () =>
    {
        var budget = Context(55, 1.2f);
        Equal(66, budget.EffectiveLimit);
        True(budget.Allows(66));
        False(budget.Allows(67));
    }),
    ("小数上限向下取整", () =>
    {
        var budget = Context(59, 1.05f);
        Equal(61, budget.EffectiveLimit);
        True(budget.Allows(61));
        False(budget.Allows(62));
    }),
    ("零余额不是无限预算", () =>
    {
        var budget = Context(0, 1.4f);
        True(budget.Allows(0));
        False(budget.Allows(1));
    }),
    ("免费订单零余额仍可推荐", () =>
    {
        var budget = Context(0, 1.2f, isFree: true);
        Equal(int.MaxValue, budget.EffectiveLimit);
        True(budget.Allows(999999));
    }),
    ("秦心类数值增益只采用运行时结果", () =>
    {
        var beforeBuff = Context(100, 1.2f);
        var afterBuff = Context(120, 1.2f);
        Equal(120, beforeBuff.EffectiveLimit);
        Equal(144, afterBuff.EffectiveLimit);
    }),
    ("正常订单保守递减", () =>
    {
        var budget = Context(100, 1.2f);
        Equal(34, budget.EstimateRemainingAfterOrder(66));
    }),
    ("免费订单不递减", () =>
    {
        var budget = Context(34, 1.2f, isFree: true);
        Equal(34, budget.EstimateRemainingAfterOrder(500));
    }),
    ("酒水退款后由运行时余额覆盖估算", () =>
    {
        var first = Context(100, 1.2f);
        Equal(20, first.EstimateRemainingAfterOrder(80));

        var afterRefund = first.WithRuntimeRemaining(50);
        Equal(60, afterRefund.EffectiveLimit);
        True(afterRefund.Allows(60));
        False(afterRefund.Allows(61));
    }),
    ("连续多轮回退预算", () =>
    {
        var first = Context(100, 1.2f);
        int afterFirst = first.EstimateRemainingAfterOrder(40);
        var second = first.AsFallback(afterFirst, 1.2f);
        Equal(72, second.EffectiveLimit);
        True(second.Allows(72));

        int afterSecond = second.EstimateRemainingAfterOrder(72);
        var third = second.AsFallback(afterSecond, 1.2f);
        Equal(0, third.EffectiveLimit);
        False(third.Allows(1));

        var freeExtra = Context(afterSecond, 1.2f, isFree: true);
        True(freeExtra.Allows(300));
        Equal(0, freeExtra.EstimateRemainingAfterOrder(300));
    }),
    ("预算被符卡降低后旧方案失效", () =>
    {
        var before = Context(100, 1.2f);
        True(before.Allows(100));

        var after = Context(60, 1.2f);
        False(after.Allows(100));
        True(after.Allows(72));
    }),
    ("免费状态不会泄漏到下一普通订单", () =>
    {
        var free = Context(0, 1.2f, isFree: true);
        True(free.Allows(500));

        var normal = Context(0, 1.2f, isFree: false);
        False(normal.Allows(500));
    }),
    ("三个游戏免费标记任意一个都生效", () =>
    {
        False(OrderBudgetContext.ResolveFreeOrder(false, false, false));
        True(OrderBudgetContext.ResolveFreeOrder(true, false, false));
        True(OrderBudgetContext.ResolveFreeOrder(false, true, false));
        True(OrderBudgetContext.ResolveFreeOrder(false, false, true));
        True(OrderBudgetContext.ResolveFreeOrder(true, true, true));
    }),
    ("低于一的异常耐受值按一处理", () =>
    {
        var budget = Context(80, 0.5f);
        Equal(80, budget.EffectiveLimit);
    }),
    ("极大预算防止整数溢出", () =>
    {
        var budget = Context(int.MaxValue, 2f);
        Equal(int.MaxValue, budget.EffectiveLimit);
        True(budget.Allows(int.MaxValue));
    }),
    ("负价格永远拒绝", () =>
    {
        False(Context(100, 1.2f).Allows(-1));
        False(Context(0, 1.2f, isFree: true).Allows(-1));
    }),
    ("只有料理允许显示座位号", () =>
    {
        True(SeatMarkerPolicy.CanCarrySeatMarker(SeatMarkerItemKind.Food));
        False(SeatMarkerPolicy.CanCarrySeatMarker(SeatMarkerItemKind.Beverage));
        False(SeatMarkerPolicy.CanCarrySeatMarker(SeatMarkerItemKind.Unknown));
    }),
    ("酒水与料理数值ID相同也不能占号", () =>
    {
        True(SeatMarkerPolicy.CanBindByNumericId(SeatMarkerItemKind.Food, 34, 34));
        False(SeatMarkerPolicy.CanBindByNumericId(SeatMarkerItemKind.Beverage, 34, 34));
        False(SeatMarkerPolicy.CanBindByNumericId(SeatMarkerItemKind.Unknown, 34, 34));
        False(SeatMarkerPolicy.CanBindByNumericId(SeatMarkerItemKind.Food, 34, 35));
    }),
    ("稀客静态预算与耐受数据有效", () =>
    {
        string dataPath = Path.Combine(Environment.CurrentDirectory, "Data", "customers_rare.json");
        using var document = JsonDocument.Parse(File.ReadAllText(dataPath));
        var customers = document.RootElement.EnumerateArray().ToList();
        True(customers.Count > 0);

        var names = new HashSet<string>();
        bool foundMomiji = false;
        foreach (var customer in customers)
        {
            string name = customer.GetProperty("name").GetString() ?? "";
            True(names.Add(name));

            double endurance = customer.GetProperty("enduranceLimit").GetDouble();
            True(endurance >= 1d);

            var prices = customer.GetProperty("price").EnumerateArray()
                .Select(value => value.GetInt32())
                .ToList();
            True(prices.Count > 0);
            True(prices.All(price => price >= 0));

            if (name == "犬走椛")
            {
                foundMomiji = true;
                Equal(1.2d, endurance);
            }
        }
        True(foundMomiji);
    })
};

int passed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        passed++;
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
        return 1;
    }
}

Console.WriteLine($"预算逻辑测试通过: {passed}/{tests.Count}");
return 0;

static OrderBudgetContext Context(int remaining, float endurance, bool isFree = false)
{
    return new OrderBudgetContext
    {
        RemainingFund = remaining,
        EnduranceLimit = endurance,
        HasRuntimeBudget = true,
        IsFreeOrder = isFree
    };
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("预期为 true，实际为 false");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("预期为 false，实际为 true");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"预期 {expected}，实际 {actual}");
}

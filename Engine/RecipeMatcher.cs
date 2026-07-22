using System.Collections.Generic;
using System.IO;
using System.Linq;
using MystiaRecommendation;

namespace MystiaRecommendation.Engine;

public class RecipeMatcher
{
    private readonly CustomerDataEngine _dataEngine;

    public RecipeMatcher(CustomerDataEngine dataEngine)
    {
        _dataEngine = dataEngine;
    }

    public List<Recommendation> CalculateByRequestTags(
        string customerName, string reqFoodTag, string reqBevTag, int maxBudget,
        HashSet<string> unlockedRecipes, HashSet<string> unlockedBeverages,
        HashSet<string> availableIngredients,
        HashSet<string> availableCookers,
        Dictionary<string, int> ingredientStocks,
        PopularTrendState popularTrend)
    {
        var customer = _dataEngine.GetCustomer(customerName);
        if (customer == null) return new();

        var positiveTags = new HashSet<string>(customer.positiveTags);
        var preferredBeverageTags = new HashSet<string>(customer.beverageTags ?? new List<string>());
        foreach (var tag in preferredBeverageTags)
            positiveTags.Add(tag);
        var negativeTags = new HashSet<string>(customer.negativeTags);
        bool hasNightingaleCooker = availableCookers != null && availableCookers.Contains("夜雀厨具");

        System.Func<RecipeInfo, bool> normalCookerFilter = r =>
            availableCookers == null || availableCookers.Count == 0
            || availableCookers.Contains(r.Cooker)
            || availableCookers.Contains("夜雀" + r.Cooker);

        System.Func<RecipeInfo, bool> stockFilter = r =>
            r.Ingredients.All(i => availableIngredients.Contains(i));

        System.Func<BeverageInfo, bool> beverageFilter = b =>
            !string.IsNullOrEmpty(reqBevTag) && b.Tags.Contains(reqBevTag);

        // 诊断：输出当前收到的订单标签
        Plugin.Instance?.Log.LogInfo($"[MystiaRec] 算法入参: customer={customerName} reqFoodTag={reqFoodTag} reqBevTag={reqBevTag} budget={maxBudget} unlockedRecipes={unlockedRecipes.Count} unlockedBevs={unlockedBeverages.Count} cookers=[{string.Join(",", availableCookers ?? new HashSet<string>())}]");

        // Step 1: 判断分支 — 食物Tag是否有匹配料理？酒水Tag是否有匹配酒水？
        var matchingRecipes = unlockedRecipes
            .Select(RecipeDatabase.GetRecipe)
            .Where(r => r != null
                && normalCookerFilter(r)
                && stockFilter(r)
                && !string.IsNullOrEmpty(reqFoodTag)
                && r.PositiveTags.Contains(reqFoodTag))
            .ToList();
        bool hasMatchingRecipe = matchingRecipes.Count > 0;

        bool hasMatchingBeverage = unlockedBeverages
            .Select(RecipeDatabase.GetBeverage)
            .Any(b => b != null && !string.IsNullOrEmpty(reqBevTag) && b.Tags.Contains(reqBevTag));

        // 判断食材扩展是否可行：有库存≥10且带reqFoodTag的食材
        bool canExpandFoodTag = !hasMatchingRecipe
            && ingredientStocks != null && ingredientStocks.Count > 0
            && availableIngredients != null && availableIngredients.Count > 0
            && availableIngredients.Any(name =>
                RecipeDatabase.GetIngredientTagIndex().TryGetValue(name, out var tags)
                && tags.Contains(reqFoodTag)
                && ingredientStocks.TryGetValue(name, out int c) && c >= 10);

        if (matchingRecipes.Count > 0)
            Plugin.Instance?.Log.LogInfo($"[MystiaRec] 匹配Tag料理: {matchingRecipes.Count}个, 食材扩展可行={canExpandFoodTag}");
        else
            Plugin.Instance?.Log.LogInfo($"[MystiaRec] 无匹配Tag料理（reqFoodTag={reqFoodTag}），食材扩展可行={canExpandFoodTag}, hasNightingale={hasNightingaleCooker}");

        if (!hasMatchingBeverage)
            Plugin.Instance?.Log.LogInfo($"[MystiaRec] 酒水匹配: 0个酒水匹配reqBevTag={reqBevTag}");

        List<MatchCandidate> candidates;
        bool needNightingale;
        string branchReqFoodTag;

        // 优先级: A(正常匹配) = C(食材扩展) > B(夜雀兜底)
        if (hasMatchingRecipe && hasMatchingBeverage)
        {
            // 分支A：料理和酒水都有匹配Tag → 正常流程 + 食材扩展补充
            candidates = BuildCandidates(
                unlockedRecipes, unlockedBeverages, maxBudget,
                positiveTags, negativeTags, popularTrend,
                r => normalCookerFilter(r) && stockFilter(r) && !string.IsNullOrEmpty(reqFoodTag) && r.PositiveTags.Contains(reqFoodTag),
                beverageFilter);
            needNightingale = false;
            branchReqFoodTag = reqFoodTag;
            Plugin.Instance?.Log.LogInfo($"[MystiaRec] 分支A: 正常匹配, {candidates.Count}个候选");

            // 食材扩展补充：匹配料理 ≤ 5 时扩充选项
            if (matchingRecipes.Count <= 5 && canExpandFoodTag)
            {
                var expandedCandidates = BuildIngredientExpandedCandidates(
                    unlockedRecipes, unlockedBeverages, maxBudget,
                    positiveTags, negativeTags, availableIngredients,
                    ingredientStocks, popularTrend,
                    normalCookerFilter, beverageFilter,
                    reqFoodTag);
                candidates.AddRange(expandedCandidates);
                Plugin.Instance?.Log.LogInfo($"[MystiaRec] 分支A+食材扩展: 新增{expandedCandidates.Count}个候选");
            }
        }
        else if (hasMatchingBeverage && canExpandFoodTag)
        {
            // 分支C：酒水匹配、料理不匹配，但有食材可补足reqFoodTag（优先级高于夜雀）
            candidates = BuildIngredientExpandedCandidates(
                unlockedRecipes, unlockedBeverages, maxBudget,
                positiveTags, negativeTags, availableIngredients,
                ingredientStocks, popularTrend,
                normalCookerFilter, beverageFilter,
                reqFoodTag);
            needNightingale = false;
            branchReqFoodTag = reqFoodTag;
            Plugin.Instance?.Log.LogInfo($"[MystiaRec] 分支C(食材扩展): 酒水匹配, 食材补足reqFoodTag={reqFoodTag}, 产生{candidates.Count}个候选");
        }
        else if ((!hasMatchingRecipe || !hasMatchingBeverage) && hasNightingaleCooker)
        {
            // 分支B：A和C都无法满足，夜雀厨具兜底 → 绕过两边Tag限制
            candidates = BuildCandidates(
                unlockedRecipes, unlockedBeverages, maxBudget,
                positiveTags, negativeTags, popularTrend,
                r => stockFilter(r),
                b => true);
            needNightingale = true;
            branchReqFoodTag = "";
            Plugin.Instance?.Log.LogInfo($"[MystiaRec] 分支B(夜雀兜底): 料理匹配={hasMatchingRecipe}, 酒水匹配={hasMatchingBeverage}, 夜雀绕过Tag限制");
        }
        else
        {
            // 无可用方案：既没有A/C条件，也没有夜雀厨具
            Plugin.Instance?.Log.LogInfo($"[MystiaRec] 无可用方案: 料理匹配={hasMatchingRecipe}, 酒水匹配={hasMatchingBeverage}, 食材扩展={canExpandFoodTag}, 夜雀={hasNightingaleCooker}");
            return new();
        }

        // Step 2: 按分数阈值降级尝试（4 → 3 → 2 → 1）
        bool loggedMissingIngredients = false;
        int bestPossibleScore = candidates.Count > 0 ? candidates.Max(c => c.Score) : 0;
        Plugin.Instance?.Log.LogInfo($"[MystiaRec] 候选总数={candidates.Count}, 最高基础分={bestPossibleScore}, 分支={(needNightingale ? "B(需夜雀)" : "A")}");

        for (int targetScore = 4; targetScore >= 1; targetScore--)
        {
            var validCandidates = CollectCandidatesAtThreshold(
                candidates, positiveTags, negativeTags, availableIngredients,
                ingredientStocks, branchReqFoodTag, targetScore, ref loggedMissingIngredients);

            if (validCandidates.Count > 0)
            {
                Plugin.Instance?.Log.LogInfo($"[MystiaRec] 阈值≥{targetScore}: {validCandidates.Count}个方案达标");
                var result = PickPriceExtremes(validCandidates);
                foreach (var rec in result)
                {
                    rec.NeedNightingale = needNightingale;
                    if (needNightingale && availableCookers != null)
                    {
                        // 匹配配方厨具对应的夜雀版本（如 "油锅" → "夜雀油锅"）
                        var recipeInfo = RecipeDatabase.GetRecipe(rec.RecipeName);
                        string expectedNightingale = "夜雀" + (recipeInfo?.Cooker ?? "");
                        var nightingaleCooker = availableCookers.FirstOrDefault(c => c == expectedNightingale)
                            ?? availableCookers.FirstOrDefault(c => c.StartsWith("夜雀"));
                        rec.RequiredCooker = nightingaleCooker ?? "夜雀厨具";
                    }
                    rec.FallbackBelowFour = targetScore < 4;
                }
                return result;
            }
        }

        Plugin.Instance?.Log.LogWarning($"[MystiaRec] 所有阈值均无达标方案，进入保底");

        // 最终保底：尽量找包含所需Tag的正分方案
        // Branch A: 必须含 reqFoodTag; Branch B: branchReqFoodTag="" 所以全部通过
        var fallback = candidates
            .Where(c => c.Score > 0 && HasRequiredFoodTag(c.Tags, branchReqFoodTag))
            .Select(c =>
            {
                var copy = c.Clone();
                copy.NeedNightingale = needNightingale;
                copy.FallbackBelowFour = true;
                copy.MissingRequiredFoodTag = !needNightingale && !HasRequiredFoodTag(c.Tags, branchReqFoodTag);
                return copy;
            })
            .ToList();

        var fallbackResult = PickPriceExtremes(fallback);
        foreach (var rec in fallbackResult)
        {
            rec.NeedNightingale = needNightingale;
            if (needNightingale && availableCookers != null)
            {
                var recipeInfo = RecipeDatabase.GetRecipe(rec.RecipeName);
                string expectedNightingale = "夜雀" + (recipeInfo?.Cooker ?? "");
                var nightingaleCooker = availableCookers.FirstOrDefault(c => c == expectedNightingale)
                    ?? availableCookers.FirstOrDefault(c => c.StartsWith("夜雀"));
                rec.RequiredCooker = nightingaleCooker ?? "夜雀厨具";
            }
            rec.FallbackBelowFour = true;
            rec.ExpectedRating = "无法满足4分";
        }
        return fallbackResult;
    }

    /// <summary>
    /// 收集达到指定分数阈值的候选组合（包括加食材后达到的）
    /// </summary>
    private List<MatchCandidate> CollectCandidatesAtThreshold(
        List<MatchCandidate> sourceCandidates,
        HashSet<string> positiveTags,
        HashSet<string> negativeTags,
        HashSet<string> availableIngredients,
        Dictionary<string, int> ingredientStocks,
        string requiredFoodTag,
        int minScore,
        ref bool loggedMissingIngredients)
    {
        var result = new List<MatchCandidate>();

        foreach (var candidate in sourceCandidates)
        {
            // 不加食材已满足分数
            if (candidate.Score >= minScore && HasRequiredFoodTag(candidate.Tags, requiredFoodTag))
            {
                result.Add(candidate.Clone());
                continue;
            }

            // 尝试加食材提分
            var enhanced = TryAddExtraIngredients(
                candidate.Clone(), positiveTags, negativeTags,
                availableIngredients, ingredientStocks, requiredFoodTag, minScore,
                ref loggedMissingIngredients);
            if (enhanced != null)
                result.Add(enhanced);
        }

        return result;
    }

    public List<Recommendation> CalculateUnknownByRequestTags(
        string reqFoodTag, string reqBevTag, int maxBudget,
        HashSet<string> unlockedRecipes, HashSet<string> unlockedBeverages,
        HashSet<string> availableIngredients,
        PopularTrendState popularTrend)
    {
        var recipes = unlockedRecipes
            .Select(RecipeDatabase.GetRecipe)
            .Where(r => r != null)
            .Where(r => r.Ingredients.All(i => availableIngredients == null || availableIngredients.Contains(i)))
            .Where(r => !string.IsNullOrEmpty(reqFoodTag) && r.PositiveTags.Contains(reqFoodTag))
            .ToList();

        var beverages = unlockedBeverages
            .Select(RecipeDatabase.GetBeverage)
            .Where(b => b != null)
            .Where(b => !string.IsNullOrEmpty(reqBevTag) && b.Tags.Contains(reqBevTag))
            .ToList();

        if (recipes.Count == 0 || beverages.Count == 0)
            return new();

        var candidates = new List<MatchCandidate>();
        foreach (var recipe in recipes)
        {
            foreach (var bev in beverages)
            {
                if (recipe.Price + bev.Price > maxBudget) continue;

                var tags = MergeTags(recipe, bev, null, 0, popularTrend);
                ResolveTagOverrides(tags);

                candidates.Add(new MatchCandidate
                {
                    Recipe = recipe,
                    Beverage = bev,
                    Tags = tags,
                    Score = tags.Count,
                    Stage = 1
                });
            }
        }

        return PickPriceExtremes(candidates);
    }

    private List<MatchCandidate> BuildCandidates(
        HashSet<string> unlockedRecipes,
        HashSet<string> unlockedBeverages,
        int maxBudget,
        HashSet<string> positiveTags,
        HashSet<string> negativeTags,
        PopularTrendState popularTrend,
        System.Func<RecipeInfo, bool> recipeFilter,
        System.Func<BeverageInfo, bool> beverageFilter)
    {
        var recipes = unlockedRecipes
            .Select(RecipeDatabase.GetRecipe)
            .Where(r => r != null)
            .Where(recipeFilter)
            .ToList();

        var beverages = unlockedBeverages
            .Select(RecipeDatabase.GetBeverage)
            .Where(b => b != null)
            .Where(beverageFilter)
            .ToList();

        var allBevs = unlockedBeverages
            .Select(RecipeDatabase.GetBeverage)
            .Where(b => b != null)
            .ToList();
        if (allBevs.Count > 0 && beverages.Count == 0)
            Plugin.Instance?.Log.LogWarning($"[MystiaRec] 酒水过滤: {allBevs.Count}个已解锁酒水中0个匹配beverageFilter, 可用标签: {string.Join(",", allBevs.SelectMany(b => b.Tags).Distinct())}");

        var candidates = new List<MatchCandidate>();
        foreach (var recipe in recipes)
        {
            foreach (var bev in beverages)
            {
                if (recipe.Price + bev.Price > maxBudget) continue;

                var tags = MergeTags(recipe, bev, null, 0, popularTrend);
                ResolveTagOverrides(tags);

                candidates.Add(new MatchCandidate
                {
                    Recipe = recipe,
                    Beverage = bev,
                    Tags = tags,
                    Score = ScoreTags(tags, positiveTags, negativeTags),
                    Stage = 1
                });
            }
        }

        return candidates;
    }

    /// <summary>
    /// 食材扩展路径：当匹配Tag的料理不够时，用高库存食材来补足 reqFoodTag。
    /// 食材自身提供 reqFoodTag，与其他任意料理+酒水组合，参与评分和价格排序。
    /// 不设价格上限，只看 reqFoodTag + 库存 ≥ 10，取库存最高者。
    /// </summary>
    private List<MatchCandidate> BuildIngredientExpandedCandidates(
        HashSet<string> unlockedRecipes,
        HashSet<string> unlockedBeverages,
        int maxBudget,
        HashSet<string> positiveTags,
        HashSet<string> negativeTags,
        HashSet<string> availableIngredients,
        Dictionary<string, int> ingredientStocks,
        PopularTrendState popularTrend,
        System.Func<RecipeInfo, bool> cookerFilter,
        System.Func<BeverageInfo, bool> beverageFilter,
        string reqFoodTag)
    {
        var result = new List<MatchCandidate>();

        // 找出合格食材：有 reqFoodTag、库存 ≥ 10，只取库存最多的那一个
        var bestIngredient = availableIngredients
            .Where(name => RecipeDatabase.GetIngredientTagIndex().TryGetValue(name, out var tags)
                && tags.Contains(reqFoodTag))
            .Where(name => ingredientStocks.TryGetValue(name, out int count) && count >= 10)
            .OrderByDescending(name => ingredientStocks.TryGetValue(name, out int c) ? c : 0)
            .Select(name => new
            {
                Name = name,
                Tags = RecipeDatabase.GetIngredientTagIndex()[name],
                Price = RecipeDatabase.GetIngredientPrice(name),
                Stock = ingredientStocks.TryGetValue(name, out int c2) ? c2 : 0
            })
            .FirstOrDefault();

        if (bestIngredient == null) return result;

        Plugin.Instance?.Log.LogInfo($"[MystiaRec] 食材扩展: 选中食材={bestIngredient.Name}(¥{bestIngredient.Price},库存{bestIngredient.Stock})");

        // 所有可用厨具能做、但不含 reqFoodTag 的料理（含Tag的已在分支A中处理）
        var expandRecipes = unlockedRecipes
            .Select(RecipeDatabase.GetRecipe)
            .Where(r => r != null && cookerFilter(r))
            .Where(r => r.Ingredients.All(i => availableIngredients.Contains(i))) // 基础食材有库存
            .Where(r => !r.PositiveTags.Contains(reqFoodTag)) // 避免和分支A重复
            .ToList();

        var beverages = unlockedBeverages
            .Select(RecipeDatabase.GetBeverage)
            .Where(b => b != null)
            .Where(beverageFilter)
            .ToList();

        foreach (var recipe in expandRecipes)
        {
            // 冲突检查：食材Tag vs 料理negativeTags
            if (recipe.NegativeTags != null && bestIngredient.Tags.Any(t => recipe.NegativeTags.Contains(t)))
                continue;

            // 冲突检查：食材Tag vs 稀客negativeTags
            if (negativeTags != null && bestIngredient.Tags.Any(t => negativeTags.Contains(t)))
                continue;

            // 食材不能已在料理的基础食材中
            if (recipe.Ingredients.Contains(bestIngredient.Name))
                continue;

            foreach (var bev in beverages)
            {
                int totalPrice = recipe.Price + bev.Price;
                if (totalPrice > maxBudget) continue;

                // 合并Tag：料理 + 酒水 + 食材
                var mergedTags = new HashSet<string>(recipe.PositiveTags);
                foreach (var tag in bev.Tags) mergedTags.Add(tag);
                foreach (var tag in bestIngredient.Tags) mergedTags.Add(tag);

                // 隐式添加"大份"（总食材≥5且料理不排斥）
                int expandedTotalIngredients = recipe.Ingredients.Count + 1;
                if (expandedTotalIngredients >= 5 && (recipe.NegativeTags == null || !recipe.NegativeTags.Contains("大份")))
                    mergedTags.Add("大份");

                ResolveTagOverrides(mergedTags);

                var candidate = new MatchCandidate
                {
                    Recipe = recipe,
                    Beverage = bev,
                    Tags = mergedTags,
                    ExtraIngredients = new List<string> { bestIngredient.Name },
                    Score = ScoreTags(mergedTags, positiveTags, negativeTags),
                    Stage = 3
                };

                result.Add(candidate);
            }
        }

        return result;
    }


    private Recommendation ToRecommendation(MatchCandidate candidate, int extraIngredientCost = 0)
    {
        var recipe = candidate.Recipe;
        var bev = candidate.Beverage;
        int netScore = candidate.Score;

        string rating;
        if (candidate.FallbackBelowFour) rating = "无法满足4分";
        else if (candidate.MissingRequiredFoodTag) rating = "缺订单Tag";
        else if (netScore >= 6) rating = "完美";
        else if (netScore >= 4) rating = "优秀";
        else if (netScore >= 2) rating = "良好";
        else rating = "一般";

        string cookerDisplay = candidate.NeedNightingale ? $"夜雀{recipe.Cooker}" : recipe.Cooker;
        var ingredients = new List<string>(recipe.Ingredients);
        foreach (var extra in candidate.ExtraIngredients)
            ingredients.Add("+" + extra);

        return new Recommendation
        {
            RecipeName = recipe.Name,
            RecipeTags = candidate.Tags.ToList(),
            Ingredients = ingredients,
            ExtraIngredients = candidate.ExtraIngredients.ToList(),
            RequiredCooker = cookerDisplay,
            BeverageName = bev.Name,
            BeverageTags = bev.Tags ?? new(),
            TotalPrice = recipe.Price + bev.Price,
            TotalExtraIngredientCost = extraIngredientCost,
            Score = netScore,
            ExpectedRating = rating,
            OverBudget = false,
            NeedNightingale = candidate.NeedNightingale,
            MissingRequiredFoodTag = candidate.MissingRequiredFoodTag,
            FallbackBelowFour = candidate.FallbackBelowFour
        };
    }

    private List<Recommendation> PickPriceExtremes(List<MatchCandidate> candidates)
    {
        // 去重：同一配方+酒水+食材组合只保留分数最高的
        var unique = candidates
            .GroupBy(c => c.Identity)
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .ToList();

        if (unique.Count == 0) return new();

        // 计算每个候选的附加食材总成本
        var withCost = unique.Select(c => new
        {
            Candidate = c,
            ExtraCost = RecipeDatabase.GetTotalExtraIngredientCost(c.ExtraIngredients)
        }).ToList();

        // 按账面价格（料理+酒水）分组，取最高价和最低价
        int maxPrice = withCost.Max(x => x.Candidate.TotalPrice);
        int minPrice = withCost.Min(x => x.Candidate.TotalPrice);

        // 最高价组中选附加食材成本最低的
        var bestExpensive = withCost
            .Where(x => x.Candidate.TotalPrice == maxPrice)
            .OrderBy(x => x.ExtraCost)
            .ThenByDescending(x => x.Candidate.Score)
            .First();

        var result = new List<Recommendation> { ToRecommendation(bestExpensive.Candidate, bestExpensive.ExtraCost) };

        // 最低价组中选附加食材成本最低的（去重：排除已选）
        if (minPrice != maxPrice || withCost.Count > 1)
        {
            var bestCheap = withCost
                .Where(x => x.Candidate.Identity != bestExpensive.Candidate.Identity)
                .OrderBy(x => x.Candidate.TotalPrice)
                .ThenBy(x => x.ExtraCost)
                .ThenByDescending(x => x.Candidate.Score)
                .FirstOrDefault();

            if (bestCheap != null)
                result.Add(ToRecommendation(bestCheap.Candidate, bestCheap.ExtraCost));
        }

        return result;
    }

    private MatchCandidate TryAddExtraIngredients(
        MatchCandidate source,
        HashSet<string> positiveTags,
        HashSet<string> negativeTags,
        HashSet<string> availableIngredients,
        Dictionary<string, int> ingredientStocks,
        string requiredFoodTag,
        int minScore,
        ref bool loggedMissingIngredients)
    {
        if (availableIngredients == null || availableIngredients.Count == 0)
        {
            if (!loggedMissingIngredients)
            {
                Plugin.Instance?.Log.LogInfo("[MystiaRec] 当前食材库存未读取到，跳过补食材方案");
                loggedMissingIngredients = true;
            }
            return null;
        }

        int maxExtra = System.Math.Max(0, 5 - source.Recipe.Ingredients.Count - source.ExtraIngredients.Count);
        if (Plugin.PluginConfig?.MaxExtraIngredients != null)
            maxExtra = System.Math.Min(maxExtra, Plugin.PluginConfig.MaxExtraIngredients.Value);
        if (maxExtra <= 0) return null;

        for (int i = 0; i < maxExtra && (source.Score < minScore || !HasRequiredFoodTag(source.Tags, requiredFoodTag)); i++)
        {
            var best = RecipeDatabase.GetIngredientTagIndex()
                .Where(kv => kv.Value.Count > 0)
                .Where(kv => !source.Recipe.Ingredients.Contains(kv.Key) && !source.ExtraIngredients.Contains(kv.Key))
                .Where(kv => availableIngredients.Contains(kv.Key))
                .Where(kv => !ConflictsWithRecipeNegativeTags(kv.Value, source.Recipe))
                .Where(kv => !ConflictsWithCustomerNegativeTags(kv.Value, negativeTags))
                .Select(kv =>
                {
                    int totalIngredientCount = source.Recipe.Ingredients.Count + source.ExtraIngredients.Count + 1;
                    var newTags = kv.Value.Where(tag => !source.Tags.Contains(tag)).ToHashSet();
                    var mergedTags = MergeTags(source.Tags, newTags, totalIngredientCount, source.Recipe.NegativeTags);
                    ResolveTagOverrides(mergedTags);
                    return new
                    {
                        Ingredient = kv.Key,
                        AddedTags = newTags,
                        Tags = mergedTags,
                        AddedAnyTag = newTags.Count > 0,
                        SatisfiesRequiredFoodTag = HasRequiredFoodTag(mergedTags, requiredFoodTag),
                        Score = ScoreTags(mergedTags, positiveTags, negativeTags)
                    };
                })
                .Where(x => x.AddedAnyTag)
                .Where(x => (x.Score > source.Score || (!HasRequiredFoodTag(source.Tags, requiredFoodTag) && x.SatisfiesRequiredFoodTag)) &&
                    !ConflictsWithRecipeNegativeTags(x.Tags, source.Recipe) &&
                    !ConflictsWithCustomerNegativeTags(x.AddedTags, negativeTags))
                .OrderByDescending(x => x.SatisfiesRequiredFoodTag)
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => ingredientStocks.TryGetValue(x.Ingredient, out int stock) ? stock : 0)
                .ThenByDescending(x => x.AddedTags.Count)
                .FirstOrDefault();

            if (best == null) break;

            source.ExtraIngredients.Add(best.Ingredient);
            source.Tags = best.Tags;
            source.Score = best.Score;
            source.Stage = 2;
        }

        return source.Score >= minScore && source.ExtraIngredients.Count > 0 && HasRequiredFoodTag(source.Tags, requiredFoodTag) ? source : null;
    }

    private static bool HasRequiredFoodTag(HashSet<string> tags, string requiredFoodTag)
    {
        return string.IsNullOrEmpty(requiredFoodTag) || tags.Contains(requiredFoodTag);
    }

    private static bool ConflictsWithRecipeNegativeTags(IEnumerable<string> tags, RecipeInfo recipe)
    {
        if (recipe?.NegativeTags == null || recipe.NegativeTags.Count == 0) return false;
        return tags.Any(recipe.NegativeTags.Contains);
    }

    private static bool ConflictsWithCustomerNegativeTags(IEnumerable<string> tags, HashSet<string> negativeTags)
    {
        if (negativeTags == null || negativeTags.Count == 0) return false;
        return tags.Any(negativeTags.Contains);
    }

    private static HashSet<string> MergeTags(RecipeInfo recipe, BeverageInfo beverage, IEnumerable<string> extraTags, int extraIngredientCount, PopularTrendState popularTrend)
    {
        var result = new HashSet<string>(recipe.PositiveTags);
        foreach (var tag in beverage.Tags)
            result.Add(tag);
        if (extraTags != null)
            foreach (var tag in extraTags)
                result.Add(tag);
        AddPopularTrendTags(result, recipe, beverage, popularTrend);
        AddImplicitServingSizeTags(result, recipe.Ingredients.Count + extraIngredientCount, recipe.NegativeTags);
        return result;
    }

    private static HashSet<string> MergeTags(IEnumerable<string> current, IEnumerable<string> extra, int totalIngredientCount, ICollection<string> recipeNegativeTags = null)
    {
        var result = new HashSet<string>(current);
        foreach (var tag in extra)
            result.Add(tag);
        AddImplicitServingSizeTags(result, totalIngredientCount, recipeNegativeTags);
        return result;
    }

    private static void AddImplicitServingSizeTags(HashSet<string> tags, int totalIngredientCount, ICollection<string> recipeNegativeTags = null)
    {
        // 食材数达到5时隐式添加"大份"，但如果料理排斥"大份"则不添加（会变黑暗物质）
        if (totalIngredientCount >= 5 && (recipeNegativeTags == null || !recipeNegativeTags.Contains("大份")))
            tags.Add("大份");
    }

    private static void AddPopularTrendTags(HashSet<string> tags, RecipeInfo recipe, BeverageInfo beverage, PopularTrendState popularTrend)
    {
        if (popularTrend == null || !popularTrend.HasAny) return;

        bool hitsLikedTrend =
            recipe.PositiveTags.Any(popularTrend.LikeFoodTags.Contains) ||
            beverage.Tags.Any(popularTrend.LikeBeverageTags.Contains);
        bool hitsHatedTrend =
            recipe.PositiveTags.Any(popularTrend.HateFoodTags.Contains) ||
            beverage.Tags.Any(popularTrend.HateBeverageTags.Contains);

        if (hitsLikedTrend)
            tags.Add("流行喜爱");
        if (hitsHatedTrend)
            tags.Add("流行厌恶");
    }

    private static int ScoreTags(HashSet<string> tags, HashSet<string> positiveTags, HashSet<string> negativeTags)
    {
        return tags.Count(positiveTags.Contains) - tags.Count(negativeTags.Contains);
    }

    /// <summary>
    /// 标签覆盖规则：强者出现时，弱者被移除。
    /// 这是游戏机制（非暗黑物质），被覆盖的标签直接消失。
    /// </summary>
    private static readonly (string Dominant, string Suppressed)[] TagOverrides = new[]
    {
        ("大份", "小巧"),
        ("灼热", "凉爽"),
        ("肉", "素"),
        ("重油", "清淡"),
        ("饱腹", "下酒"),
    };

    private static void ResolveTagOverrides(HashSet<string> tags)
    {
        foreach (var (dominant, suppressed) in TagOverrides)
        {
            if (tags.Contains(dominant) && tags.Contains(suppressed))
                tags.Remove(suppressed);
        }
    }
}

public class Recommendation
{
    public string RecipeName { get; set; }
    public List<string> RecipeTags { get; set; } = new();
    public List<string> Ingredients { get; set; } = new();
    public List<string> ExtraIngredients { get; set; } = new();
    public string RequiredCooker { get; set; }
    public string BeverageName { get; set; }
    public List<string> BeverageTags { get; set; } = new();
    public int TotalPrice { get; set; }
    public int TotalExtraIngredientCost { get; set; }
    public int Score { get; set; }
    public string ExpectedRating { get; set; }
    public bool OverBudget { get; set; }
    public bool NeedNightingale { get; set; }
    public bool MissingRequiredFoodTag { get; set; }
    public bool FallbackBelowFour { get; set; }
}

internal class MatchCandidate
{
    public RecipeInfo Recipe { get; set; }
    public BeverageInfo Beverage { get; set; }
    public HashSet<string> Tags { get; set; } = new();
    public List<string> ExtraIngredients { get; set; } = new();
    public int Score { get; set; }
    public int Stage { get; set; }
    public bool NeedNightingale { get; set; }
    public bool MissingRequiredFoodTag { get; set; }
    public bool FallbackBelowFour { get; set; }
    public int TotalPrice => Recipe.Price + Beverage.Price;
    public string Identity => Recipe.Name + "\u001f" + Beverage.Name + "\u001f" + string.Join("|", ExtraIngredients);

    public MatchCandidate Clone()
    {
        return new MatchCandidate
        {
            Recipe = Recipe,
            Beverage = Beverage,
            Tags = new HashSet<string>(Tags),
            ExtraIngredients = new List<string>(ExtraIngredients),
            Score = Score,
            Stage = Stage,
            NeedNightingale = NeedNightingale,
            MissingRequiredFoodTag = MissingRequiredFoodTag,
            FallbackBelowFour = FallbackBelowFour
        };
    }
}

public enum UnlockType
{
    Self,           // 初始自带
    Bond,           // 羁绊等级解锁
    LevelUp,        // 角色等级解锁
    QuestOrEvent,   // 任务/联动活动
    Shop,           // 商店购买
    Special,        // 特殊（惩罚符卡/制作失败等）
    Unknown         // 无法确定，回退到 HaveRecipe
}

public class UnlockCondition
{
    public UnlockType Type { get; set; }
    public string BondName { get; set; }    // Bond: 稀客名称
    public int BondLevel { get; set; }      // Bond: 所需羁绊等级
    public int RequiredLevel { get; set; }  // LevelUp: 所需角色等级
    public string Area { get; set; }        // LevelUp: 所需地区（null=不限）
    public string Description { get; set; } // Quest/Shop: 描述文本
    public string ShopName { get; set; }    // Shop: 商店名称

    /// <summary>
    /// 是否可以通过运行时数据精确判断（Bond/LevelUp/Self）
    /// </summary>
    public bool CanDetect =>
        Type == UnlockType.Self ||
        Type == UnlockType.Bond ||
        Type == UnlockType.LevelUp;

    /// <summary>
    /// 是否为始终解锁
    /// </summary>
    public bool IsAlwaysUnlocked => Type == UnlockType.Self;
}

public class RecipeInfo
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public string Name { get; set; }
    public List<string> Ingredients { get; set; } = new();
    public List<string> PositiveTags { get; set; } = new();
    public List<string> NegativeTags { get; set; } = new();
    public string Cooker { get; set; }
    public int Price { get; set; }
    public int Dlc { get; set; }
    public int Level { get; set; }
    public int BaseCookTime { get; set; }
    public UnlockCondition Unlock { get; set; }
}

public class BeverageInfo
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<string> Tags { get; set; } = new();
    public int Price { get; set; }
    public int Dlc { get; set; }
}

public static class RecipeDatabase
{
    private static Dictionary<string, RecipeInfo> _recipes = new();
    private static Dictionary<string, BeverageInfo> _beverages = new();
    private static Dictionary<int, RecipeInfo> _recipesById = new();
    private static Dictionary<int, BeverageInfo> _beveragesById = new();
    private static Dictionary<string, HashSet<string>> _ingredientTagIndex = new();
    private static Dictionary<string, int> _ingredientPrices = new();
    private static Dictionary<int, string> _ingredientNamesByGameId = new();
    private static bool _loaded = false;

    public static int RecipeCount => _recipes.Count;
    public static int BeverageCount => _beverages.Count;

    public static void LoadFromDirectory(string dataDir)
    {
        if (_loaded) return;
        try
        {
            var recipePath = Path.Combine(dataDir, "recipes.json");
            if (File.Exists(recipePath))
            {
                var arr = SimpleJson.ParseArray(File.ReadAllText(recipePath));
                foreach (var t in arr)
                {
                    var r = new RecipeInfo();
                    r.Id = t.ContainsKey("id") ? SimpleJson.ToInt(t["id"]) : 0;
                    r.RecipeId = t.ContainsKey("recipeId") ? SimpleJson.ToInt(t["recipeId"]) : 0;
                    r.Name = t.ContainsKey("name") ? SimpleJson.ToString(t["name"]) : "";
                    r.Cooker = t.ContainsKey("cooker") ? SimpleJson.ToString(t["cooker"]) : "";
                    r.Price = t.ContainsKey("price") ? SimpleJson.ToInt(t["price"]) : 0;
                    r.Dlc = t.ContainsKey("dlc") ? SimpleJson.ToInt(t["dlc"]) : 0;
                    r.Level = t.ContainsKey("level") ? SimpleJson.ToInt(t["level"]) : 0;
                    r.BaseCookTime = t.ContainsKey("baseCookTime") ? SimpleJson.ToInt(t["baseCookTime"]) : 0;
                    r.Ingredients = t.ContainsKey("ingredients") ? SimpleJson.ToStringList(t["ingredients"]) : new();
                    r.PositiveTags = t.ContainsKey("positiveTags") ? SimpleJson.ToStringList(t["positiveTags"]) : new();
                    r.NegativeTags = t.ContainsKey("negativeTags") ? SimpleJson.ToStringList(t["negativeTags"]) : new();
                    r.Unlock = ParseUnlockCondition(t);
                    if (!string.IsNullOrEmpty(r.Name))
                    {
                        _recipes[r.Name] = r;
                        _recipesById[r.Id] = r;
                        IndexIngredients(r);
                    }
                }
                Plugin.Instance?.Log.LogInfo("[MystiaRec] Loaded " + _recipes.Count + " recipes");
            }
            var bevPath = Path.Combine(dataDir, "beverages.json");
            if (File.Exists(bevPath))
            {
                var arr = SimpleJson.ParseArray(File.ReadAllText(bevPath));
                foreach (var t in arr)
                {
                    var b = new BeverageInfo();
                    b.Id = t.ContainsKey("id") ? SimpleJson.ToInt(t["id"]) : 0;
                    b.Name = t.ContainsKey("name") ? SimpleJson.ToString(t["name"]) : "";
                    b.Price = t.ContainsKey("price") ? SimpleJson.ToInt(t["price"]) : 0;
                    b.Dlc = t.ContainsKey("dlc") ? SimpleJson.ToInt(t["dlc"]) : 0;
                    b.Tags = t.ContainsKey("tags") ? SimpleJson.ToStringList(t["tags"]) : new();
                    if (!string.IsNullOrEmpty(b.Name))
                    {
                        _beverages[b.Name] = b;
                        _beveragesById[b.Id] = b;
                    }
                }
                Plugin.Instance?.Log.LogInfo("[MystiaRec] Loaded " + _beverages.Count + " beverages");
            }
        }
        catch (System.Exception e)
        {
            Plugin.Instance?.Log.LogError("[MystiaRec] LoadFromDirectory error: " + e.Message);
        }

        try
        {
            var ingPath = Path.Combine(dataDir, "ingredients.json");
            if (File.Exists(ingPath))
            {
                var ingArr = SimpleJson.ParseArray(File.ReadAllText(ingPath));
                foreach (var t in ingArr)
                {
                    var name = t.ContainsKey("name") ? SimpleJson.ToString(t["name"]) : "";
                    var price = t.ContainsKey("price") ? SimpleJson.ToInt(t["price"]) : 0;
                    var tags = t.ContainsKey("tags") ? SimpleJson.ToStringList(t["tags"]) : new List<string>();
                    if (!string.IsNullOrEmpty(name))
                    {
                        _ingredientPrices[name] = price;
                        _ingredientTagIndex[name] = tags.Where(tag => !string.IsNullOrEmpty(tag)).ToHashSet();
                        // 预注册食材的游戏ID，避免运行时扫描遗漏高ID的DLC食材
                        if (t.ContainsKey("id"))
                        {
                            int ingId = SimpleJson.ToInt(t["id"]);
                            if (ingId > 0 && !_ingredientNamesByGameId.ContainsKey(ingId))
                                _ingredientNamesByGameId[ingId] = name;
                        }
                    }
                }
                Plugin.Instance?.Log.LogInfo("[MystiaRec] Loaded " + _ingredientPrices.Count + " ingredient prices (" + _ingredientNamesByGameId.Count + " IDs pre-registered)");
            }
        }
        catch (System.Exception e)
        {
            Plugin.Instance?.Log.LogWarning("[MystiaRec] Load ingredients.json error: " + e.Message);
        }

        _loaded = true;
    }

    public static RecipeInfo GetRecipe(string name) => _recipes.TryGetValue(name, out var r) ? r : null;
    public static BeverageInfo GetBeverage(string name) => _beverages.TryGetValue(name, out var b) ? b : null;
    public static RecipeInfo GetRecipeById(int id) => _recipesById.TryGetValue(id, out var r) ? r : null;
    public static BeverageInfo GetBeverageById(int id) => _beveragesById.TryGetValue(id, out var b) ? b : null;
    public static IEnumerable<RecipeInfo> GetAllRecipes() => _recipes.Values;
    public static IEnumerable<BeverageInfo> GetAllBeverages() => _beverages.Values;
    public static IReadOnlyDictionary<string, HashSet<string>> GetIngredientTagIndex() => _ingredientTagIndex;
    public static int GetIngredientPrice(string name) => _ingredientPrices.TryGetValue(name, out var price) ? price : 0;
    public static int GetTotalExtraIngredientCost(List<string> extraIngredients) =>
        extraIngredients?.Sum(name => GetIngredientPrice(name)) ?? 0;
    public static string ResolveIngredientName(int id) => _ingredientNamesByGameId.TryGetValue(id, out var name) ? name : "";
    public static IEnumerable<KeyValuePair<int, string>> GetKnownIngredientIds() => _ingredientNamesByGameId;
    public static bool RegisterIngredientId(int id, string ingredient)
    {
        if (id <= 0 || string.IsNullOrEmpty(ingredient) || !IsKnownIngredient(ingredient)) return false;
        if (_ingredientNamesByGameId.TryGetValue(id, out var existing) && existing == ingredient) return false;
        _ingredientNamesByGameId[id] = ingredient;
        return true;
    }
    public static bool IsKnownIngredient(string value) => !string.IsNullOrEmpty(value) && _ingredientTagIndex.ContainsKey(value);
    public static string ResolveIngredientName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (_ingredientTagIndex.ContainsKey(value)) return value;

        var match = _ingredientTagIndex.Keys
            .FirstOrDefault(k => value.Contains(k) || k.Contains(value));
        return match ?? "";
    }

    /// <summary>
    /// 从 recipes.json 的 unlock 字段解析解锁条件
    /// </summary>
    private static UnlockCondition ParseUnlockCondition(Dictionary<string, object> recipeDict)
    {
        var result = new UnlockCondition { Type = UnlockType.Unknown };
        if (!recipeDict.TryGetValue("unlock", out var unlockObj) || unlockObj == null)
            return result;

        if (!(unlockObj is Dictionary<string, object> unlockDict))
            return result;

        var typeStr = unlockDict.TryGetValue("type", out var t) ? SimpleJson.ToString(t) : "";
        switch (typeStr)
        {
            case "self":
                result.Type = UnlockType.Self;
                break;
            case "bond":
                result.Type = UnlockType.Bond;
                result.BondName = unlockDict.TryGetValue("bondName", out var bn) ? SimpleJson.ToString(bn) : "";
                result.BondLevel = unlockDict.TryGetValue("bondLevel", out var bl) ? SimpleJson.ToInt(bl) : 0;
                break;
            case "levelup":
                result.Type = UnlockType.LevelUp;
                result.RequiredLevel = unlockDict.TryGetValue("level", out var lv) ? SimpleJson.ToInt(lv) : 0;
                result.Area = unlockDict.TryGetValue("area", out var ar) ? SimpleJson.ToString(ar) : null;
                break;
            case "quest_or_event":
                result.Type = UnlockType.QuestOrEvent;
                result.Description = unlockDict.TryGetValue("description", out var desc) ? SimpleJson.ToString(desc) : "";
                break;
            case "shop":
                result.Type = UnlockType.Shop;
                result.ShopName = unlockDict.TryGetValue("shopName", out var sn) ? SimpleJson.ToString(sn) : "";
                result.Description = unlockDict.TryGetValue("description", out var sd) ? SimpleJson.ToString(sd) : "";
                break;
            case "special":
                result.Type = UnlockType.Special;
                break;
            default:
                result.Type = UnlockType.Unknown;
                break;
        }
        return result;
    }

    private static void IndexIngredients(RecipeInfo recipe)
    {
        foreach (var ingredient in recipe.Ingredients)
        {
            if (string.IsNullOrEmpty(ingredient)) continue;
            if (HasExplicitIngredientTags(ingredient)) continue;

            if (!_ingredientTagIndex.TryGetValue(ingredient, out var tags))
            {
                tags = new HashSet<string>();
                _ingredientTagIndex[ingredient] = tags;
            }

            foreach (var tag in InferIngredientTags(recipe, ingredient))
                tags.Add(tag);
        }

        AddKnownIngredientTags();
        AddKnownIngredientIds();
    }

    private static void AddKnownIngredientTags()
    {
        SetKnownIngredientTags("蜂蜜", "甜");
        SetKnownIngredientTags("猪肉", "肉");
        SetKnownIngredientTags("牛肉", "肉");
        SetKnownIngredientTags("鹿肉", "肉");
        SetKnownIngredientTags("野猪肉", "肉", "山珍");
        SetKnownIngredientTags("黑毛猪肉", "肉", "高级");
        SetKnownIngredientTags("蝉蜕", "猎奇", "重油");
        SetKnownIngredientTags("八目鳗", "水产", "烧烤");
        SetKnownIngredientTags("海苔", "素", "家常", "汤羹");
        SetKnownIngredientTags("豆腐", "素", "家常", "清淡");
    }

    private static void AddKnownIngredientIds()
    {
        AddKnownIngredientId(24, "蜂蜜");
    }

    private static void AddKnownIngredientId(int id, string ingredient)
    {
        if (string.IsNullOrEmpty(ingredient)) return;
        if (!_ingredientNamesByGameId.ContainsKey(id))
            _ingredientNamesByGameId[id] = ingredient;
    }

    private static void AddKnownIngredientTag(string ingredient, string tag)
    {
        if (!_ingredientTagIndex.TryGetValue(ingredient, out var tags))
        {
            tags = new HashSet<string>();
            _ingredientTagIndex[ingredient] = tags;
        }
        tags.Add(tag);
    }

    private static void SetKnownIngredientTags(string ingredient, params string[] tags)
    {
        if (string.IsNullOrEmpty(ingredient)) return;
        _ingredientTagIndex[ingredient] = tags.Where(t => !string.IsNullOrEmpty(t)).ToHashSet();
    }

    private static bool HasExplicitIngredientTags(string ingredient)
    {
        return _ingredientTagIndex.TryGetValue(ingredient, out var tags) && tags.Count > 0;
    }

    private static HashSet<string> InferIngredientTags(RecipeInfo recipe, string ingredient)
    {
        var tags = new HashSet<string>(recipe.PositiveTags);
        foreach (var other in recipe.Ingredients)
        {
            if (other == ingredient) continue;
            var otherRecipe = GetRecipeBySingleIngredient(other);
            if (otherRecipe != null)
                tags.ExceptWith(otherRecipe.PositiveTags);
        }
        return tags;
    }

    private static RecipeInfo GetRecipeBySingleIngredient(string ingredient)
    {
        return _recipes.Values
            .Where(r => r.Ingredients.Count == 1 && r.Ingredients.Contains(ingredient))
            .OrderBy(r => r.PositiveTags.Count)
            .FirstOrDefault();
    }
}

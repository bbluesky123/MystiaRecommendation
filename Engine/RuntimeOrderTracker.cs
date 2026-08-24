using GameData.Core.Collections;
using GameData.RunTime.Common;
using NightScene.CookingUtility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MystiaRecommendation.Engine;

internal enum RecommendationTrackingState
{
    AwaitingCook,
    Cooking,
    Completed
}

internal sealed class RareDishAssignment
{
    public long Id { get; init; }
    public int CardId { get; init; }
    public int DeskCode { get; init; }
    public int ExpectedFoodId { get; init; }
    public CookController Controller { get; init; }
    public Sellable Result { get; set; }
    public string ResultGuid { get; set; } = "";
    public bool FinalResultResolved { get; set; }
    public bool Completed { get; set; }
    public bool Extracted { get; set; }
    public bool InTray { get; set; }
    public int TrayIndex { get; set; } = -1;
}

/// <summary>
/// 将玩家实际开始制作的料理与稀客推荐自动绑定。
/// 同料理冲突按推荐卡片出现顺序处理；普通订单不会占用稀客队列。
/// </summary>
internal static class RuntimeOrderTracker
{
    private sealed class CardMatch
    {
        public int CardId;
        public CustomerRecommendation Card;
        public int Quality;
        public List<int> RecommendationIndexes = new();
    }

    private static readonly Dictionary<long, RareDishAssignment> _assignments = new();
    private static long _nextAssignmentId = 1;

    internal static IReadOnlyCollection<RareDishAssignment> Assignments => _assignments.Values;

    internal static void OnCookStarted(CookController controller, Sellable result, Recipe recipe)
    {
        try
        {
            if (controller == null || recipe == null || result == null) return;

            int foodId = recipe.FoodID;
            var observedExtras = ReadModifierNames(result, out bool extrasReliable);
            Plugin.Instance?.Log?.LogInfo(
                $"[MystiaRec] 厨具开始制作: foodId={foodId} recipe={recipe} " +
                $"extras=[{string.Join(",", observedExtras)}] reliable={extrasReliable} " +
                $"altered={SafeReadAltered(result)} cooker={controller.Cooker?.Type}/{controller.Cooker?.Series}");
            var matches = new List<CardMatch>();
            foreach (var pair in Plugin.ActiveRecommendations)
            {
                var card = pair.Value;
                if (card == null || card.TrackingState != RecommendationTrackingState.AwaitingCook)
                    continue;

                int bestQuality = 0;
                var indexes = new List<int>();
                for (int i = 0; i < card.Recommendations.Count; i++)
                {
                    int quality = MatchRecommendation(card.Recommendations[i], controller, result, recipe);
                    if (quality <= 0) continue;
                    if (quality > bestQuality)
                    {
                        bestQuality = quality;
                        indexes.Clear();
                    }
                    if (quality == bestQuality)
                        indexes.Add(i);
                }

                if (bestQuality > 0)
                {
                    matches.Add(new CardMatch
                    {
                        CardId = pair.Key,
                        Card = card,
                        Quality = bestQuality,
                        RecommendationIndexes = indexes
                    });
                }
            }

            if (matches.Count == 0)
            {
                Plugin.Instance?.Log?.LogInfo($"[MystiaRec] 本次料理未匹配待制作稀客订单: foodId={foodId}");
                return;
            }

            // 精确食材匹配优先；同等匹配按推荐出现先后分配。
            int highestQuality = matches.Max(m => m.Quality);
            var chosen = matches
                .Where(m => m.Quality == highestQuality)
                .OrderBy(m => m.Card.OrderSequence)
                .ThenBy(m => m.CardId)
                .First();

            int selectedIndex = chosen.RecommendationIndexes.Count == 1
                ? chosen.RecommendationIndexes[0]
                : -2; // A/B 料理完全相同，通常只剩酒水不同；保留完整卡片。

            long assignmentId = _nextAssignmentId++;
            var assignment = new RareDishAssignment
            {
                Id = assignmentId,
                CardId = chosen.CardId,
                DeskCode = chosen.Card.DeskCode,
                ExpectedFoodId = foodId,
                Controller = controller,
                Result = result,
                ResultGuid = ReadRuntimeGuid(result)
            };

            chosen.Card.MatchedRecommendationIndex = selectedIndex;
            chosen.Card.TrackingState = RecommendationTrackingState.Cooking;
            chosen.Card.ActiveAssignmentId = assignmentId;
            _assignments[assignmentId] = assignment;

            string plan = selectedIndex >= 0 ? ((char)('A' + selectedIndex)).ToString() : "A/B同料理";
            Plugin.Instance?.Log?.LogInfo(
                $"[MystiaRec] 自动绑定料理: 座位{chosen.Card.DeskCode + 1} {chosen.Card.CustomerName} " +
                $"foodId={foodId} 方案={plan} assignment={assignmentId}");
        }
        catch (Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 自动识别制作方案失败: " + e.Message);
        }
    }

    internal static void OnCookFinished(CookController controller)
    {
        try
        {
            var assignment = _assignments.Values
                .Where(a => a.Controller == controller && !a.Completed)
                .OrderByDescending(a => a.Id)
                .FirstOrDefault();
            if (assignment == null) return;

            Sellable finalResult = assignment.FinalResultResolved ? assignment.Result : null;
            try { finalResult ??= controller.Result ?? controller.LastResult; } catch { }
            int finalFoodId = -1;
            try { finalFoodId = finalResult?.Id ?? -1; } catch { }

            int darkMatterId = -1;
            try { darkMatterId = RunTimeStorage.DARK_MATTER_ID; } catch { }

            bool failed = finalResult == null
                || finalFoodId < 0
                || finalFoodId == darkMatterId
                || finalFoodId != assignment.ExpectedFoodId;

            if (!Plugin.ActiveRecommendations.TryGetValue(assignment.CardId, out var card))
            {
                _assignments.Remove(assignment.Id);
                return;
            }

            if (failed)
            {
                card.MatchedRecommendationIndex = -1;
                card.TrackingState = RecommendationTrackingState.AwaitingCook;
                card.ActiveAssignmentId = 0;
                _assignments.Remove(assignment.Id);
                Plugin.Instance?.Log?.LogInfo(
                    $"[MystiaRec] 料理失败，恢复完整方案: 座位{card.DeskCode + 1} " +
                    $"expected={assignment.ExpectedFoodId} actual={finalFoodId} darkMatter={darkMatterId}");
                return;
            }

            assignment.Result = finalResult;
            assignment.ResultGuid = ReadRuntimeGuid(finalResult);
            assignment.Completed = true;
            card.TrackingState = RecommendationTrackingState.Completed;
            Plugin.Instance?.Log?.LogInfo(
                $"[MystiaRec] 稀客料理完成: 座位{card.DeskCode + 1} foodId={finalFoodId}");
        }
        catch (Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 处理料理完成状态失败: " + e.Message);
        }
    }

    internal static void OnFinalFoodResolved(CookController controller, Sellable finalResult)
    {
        try
        {
            var assignment = _assignments.Values
                .Where(a => a.Controller == controller && !a.Completed)
                .OrderByDescending(a => a.Id)
                .FirstOrDefault();
            if (assignment == null || finalResult == null) return;
            assignment.Result = finalResult;
            assignment.ResultGuid = ReadRuntimeGuid(finalResult);
            assignment.FinalResultResolved = true;
        }
        catch { }
    }

    internal static void OnDishExtracted(CookController controller)
    {
        try
        {
            var assignment = _assignments.Values
                .Where(a => a.Controller == controller && a.Completed && !a.Extracted)
                .OrderByDescending(a => a.Id)
                .FirstOrDefault();
            if (assignment != null)
                assignment.Extracted = true;
        }
        catch { }
    }

    internal static void OnDishReceived(Sellable received, int trayIndex)
    {
        try
        {
            if (received == null || trayIndex < 0) return;

            var pending = _assignments.Values
                .Where(a => a.Completed && a.TrayIndex < 0)
                .OrderBy(a => a.Id)
                .ToList();
            if (pending.Count == 0) return;

            var assignment = pending.FirstOrDefault(a => IsSameSellable(a.Result, received));
            if (assignment == null)
            {
                int receivedFoodId = -1;
                try { receivedFoodId = received.Id; } catch { }

                // 游戏若在入托盘时重建 Sellable，则按料理 ID 分配给最早等待的稀客订单。
                // 这也符合“相同料理优先给先出现的稀客订单”的既定规则。
                assignment = pending.FirstOrDefault(a => a.ExpectedFoodId == receivedFoodId);
            }
            if (assignment == null) return;

            // Receive/RecieveInternal 的参数可能不是 FixedList 最终保存的同一个 IL2CPP 包装对象。
            // 以游戏托盘对应格子内的实际对象为准，避免后续引用一致性检查失败。
            assignment.Result = ReadTrayElement(trayIndex) ?? received;
            assignment.ResultGuid = ReadRuntimeGuid(assignment.Result);
            assignment.TrayIndex = trayIndex;
            assignment.Extracted = true;
            assignment.InTray = true;
            Plugin.Instance?.Log?.LogInfo(
                $"[MystiaRec] 稀客料理进入托盘: 座位{assignment.DeskCode + 1} " +
                $"foodId={assignment.ExpectedFoodId} trayIndex={trayIndex}");
        }
        catch (Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 绑定托盘料理失败: " + e.Message);
        }
    }

    internal static void OnDishDelivered(Sellable delivered)
    {
        try
        {
            if (delivered == null) return;
            var active = _assignments.Values
                .Where(a => a.InTray)
                .OrderBy(a => a.Id)
                .ToList();
            if (active.Count == 0) return;

            var assignment = active.FirstOrDefault(a => IsSameSellable(a.Result, delivered));
            if (assignment == null)
            {
                int deliveredFoodId = -1;
                try { deliveredFoodId = delivered.Id; } catch { }
                assignment = active.FirstOrDefault(a => a.ExpectedFoodId == deliveredFoodId);
            }
            if (assignment == null) return;

            assignment.InTray = false;
            assignment.TrayIndex = -1;
            Plugin.Instance?.Log?.LogInfo(
                $"[MystiaRec] 稀客料理离开托盘: 座位{assignment.DeskCode + 1} " +
                $"foodId={assignment.ExpectedFoodId}");
        }
        catch (Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 清理托盘料理标记失败: " + e.Message);
        }
    }

    private static Sellable ReadTrayElement(int trayIndex)
    {
        try
        {
            var elements = GameData.RunTime.NightSceneUtility.IzakayaTray.Instance?.Tray?.Elements;
            if (elements != null && trayIndex >= 0 && trayIndex < elements.Length)
                return elements[trayIndex];
        }
        catch { }
        return null;
    }

    internal static void RemoveForCard(int cardId)
    {
        var ids = _assignments.Values
            .Where(a => a.CardId == cardId)
            .Select(a => a.Id)
            .ToList();
        foreach (var id in ids)
            _assignments.Remove(id);
    }

    internal static void RemoveForDesk(int deskCode)
    {
        var ids = _assignments.Values
            .Where(a => a.DeskCode == deskCode)
            .Select(a => a.Id)
            .ToList();
        foreach (var id in ids)
            _assignments.Remove(id);
    }

    internal static void Reset()
    {
        _assignments.Clear();
        _nextAssignmentId = 1;
    }

    private static int MatchRecommendation(
        Recommendation recommendation,
        CookController controller,
        Sellable result,
        Recipe actualRecipe)
    {
        if (recommendation == null) return 0;
        int foodId = actualRecipe?.FoodID ?? -1;
        int expectedFoodId = recommendation.RecipeFoodId;
        if (expectedFoodId <= 0)
            expectedFoodId = RecipeDatabase.GetRecipe(recommendation.RecipeName)?.FoodId ?? -1;
        if (expectedFoodId != foodId) return 0;

        if (!CookerMatches(recommendation, controller, actualRecipe)) return 0;

        var expectedExtras = (recommendation.ExtraIngredients ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        var actualExtras = ReadModifierNames(result, out bool reliable);

        if (expectedExtras.Count == 0)
            return actualExtras.Count == 0 ? 2 : 0;

        if (reliable)
            return expectedExtras.SequenceEqual(actualExtras, StringComparer.Ordinal) ? 2 : 0;

        // 游戏版本未暴露完整食材 ID 时，只允许数量一致的弱匹配；不会抢占可精确匹配的方案。
        return actualExtras.Count == expectedExtras.Count ? 1 : 0;
    }

    private static bool CookerMatches(Recommendation recommendation, CookController controller, Recipe actualRecipe)
    {
        try
        {
            var recipe = RecipeDatabase.GetRecipe(recommendation.RecipeName);
            string actualType = actualRecipe?.CookerType.ToString() ?? controller.ChosenRecipe?.CookerType.ToString() ?? "";
            string expectedType = ToCookerEnumName(recipe?.Cooker ?? recommendation.RequiredCooker);
            if (!string.Equals(actualType, expectedType, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!recommendation.NeedNightingale) return true;
            return string.Equals(controller.Cooker?.Series.ToString(), "Sparrow", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ToCookerEnumName(string cooker)
    {
        cooker = (cooker ?? "").Replace("夜雀", "");
        return cooker switch
        {
            "煮锅" => "Pot",
            "烧烤架" => "Grill",
            "油锅" => "Fryer",
            "蒸锅" => "Steamer",
            "料理台" => "CuttingBoard",
            _ => cooker
        };
    }

    private static List<string> ReadModifierNames(Sellable result, out bool reliable)
    {
        reliable = true;
        var names = new List<string>();
        try
        {
            var modifiers = result.Modifier;
            if (modifiers == null) return names;
            for (int i = 0; i < modifiers.Length; i++)
            {
                int id = modifiers[i];
                if (id <= 0) continue;
                string name = RecipeDatabase.ResolveIngredientName(id);
                if (string.IsNullOrEmpty(name))
                {
                    reliable = false;
                    name = "#" + id;
                }
                names.Add(name);
            }
        }
        catch
        {
            reliable = false;
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static bool SafeReadAltered(Sellable result)
    {
        try { return result?.Altered == true; }
        catch { return false; }
    }

    internal static string ReadRuntimeGuid(Sellable sellable)
    {
        try
        {
            if (sellable?.RunTimeGUID.HasValue == true)
                return sellable.RunTimeGUID.Value.ToString();
        }
        catch { }
        return "";
    }

    internal static bool IsSameSellable(Sellable left, Sellable right)
    {
        if (left == null || right == null) return false;
        try
        {
            if (left.Pointer != IntPtr.Zero && left.Pointer == right.Pointer)
                return true;
        }
        catch { }

        string leftGuid = ReadRuntimeGuid(left);
        string rightGuid = ReadRuntimeGuid(right);
        return !string.IsNullOrEmpty(leftGuid)
            && string.Equals(leftGuid, rightGuid, StringComparison.Ordinal);
    }
}

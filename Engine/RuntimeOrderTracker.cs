using GameData.Core.Collections;
using GameData.RunTime.Common;
using NightScene.CookingUtility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

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
    public bool InStorage { get; set; }
    public string StorageSignature { get; set; } = "";
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
    private sealed class StorageDishVisual
    {
        public Sellable Dish;
        public Transform Transform;
    }

    private static readonly Dictionary<int, StorageDishVisual> _storageVisuals = new();
    private static long _storageExtractionAssignmentId;
    private static bool _storagePanelOpen;
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

            // 卡片转入左侧前先锁定一瓶酒水。A/B 料理完全相同时，也在这里按可分配库存
            // 选择一个确定酒水；无法平替则不建立料理绑定，卡片留在右侧等待 F5。
            if (!Plugin.TryLockBeverageForCook(
                    chosen.CardId, chosen.Card, chosen.RecommendationIndexes, out int selectedIndex))
                return;

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
            if (selectedIndex >= 0)
            {
                // 从右上角完整决策卡切换到桌边紧凑卡，不能沿用决策区坐标。
                chosen.Card.DragX = null;
                chosen.Card.DragY = null;
            }
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
                Plugin.ReleaseBeverageReservation(assignment.CardId);
                card.MatchedRecommendationIndex = -1;
                card.TrackingState = RecommendationTrackingState.AwaitingCook;
                card.ActiveAssignmentId = 0;
                card.DragX = null;
                card.DragY = null;
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

            var completed = _assignments.Values
                .Where(a => a.Completed)
                .OrderBy(a => a.Id)
                .ToList();
            if (completed.Count == 0) return;

            // Receive 会间接调用 RecieveInternal；两个入口都被观察时，先按实例身份命中
            // 已绑定的同一份料理，避免一次入托盘误占第二个同名订单。
            var assignment = completed.FirstOrDefault(a => IsSameSellable(a.Result, received));
            if (assignment == null && _storageExtractionAssignmentId > 0)
            {
                // 从储藏区取出前已经由玩家点中的具体储藏条目锁定绑定。即使游戏
                // 为取出的料理重建对象，也不需要在其他料理之间按时间或名称猜测。
                _assignments.TryGetValue(_storageExtractionAssignmentId, out assignment);
            }
            if (assignment == null)
            {
                int receivedFoodId = SafeReadFoodId(received);

                // 仅用于厨具首次出锅时游戏重建 Sellable 的兼容路径；已经在托盘或
                // 储藏区的同名料理不参与，绝不会跨储藏条目按时间分配。
                assignment = completed
                    .Where(a => !a.InTray && !a.InStorage && a.TrayIndex < 0)
                    .FirstOrDefault(a => a.ExpectedFoodId == receivedFoodId);
            }
            if (assignment == null) return;

            // Receive/RecieveInternal 的参数可能不是 FixedList 最终保存的同一个 IL2CPP 包装对象。
            // 以游戏托盘对应格子内的实际对象为准，避免后续引用一致性检查失败。
            assignment.Result = ReadTrayElement(trayIndex) ?? received;
            assignment.ResultGuid = ReadRuntimeGuid(assignment.Result);
            assignment.TrayIndex = trayIndex;
            assignment.Extracted = true;
            assignment.InTray = true;
            assignment.InStorage = false;
            assignment.StorageSignature = "";
            if (_storageExtractionAssignmentId == assignment.Id)
                _storageExtractionAssignmentId = 0;
            Plugin.Instance?.Log?.LogInfo(
                $"[MystiaRec] 稀客料理进入托盘: 座位{assignment.DeskCode + 1} " +
                $"foodId={assignment.ExpectedFoodId} trayIndex={trayIndex}");
        }
        catch (Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 绑定托盘料理失败: " + e.Message);
        }
    }

    internal static long OnDishReturnStarted(Sellable stored)
    {
        if (stored == null) return 0;
        try
        {
            return _assignments.Values
                .Where(a => a.Completed && a.InTray)
                .FirstOrDefault(a => IsSameSellable(a.Result, stored))?.Id ?? 0;
        }
        catch { return 0; }
    }

    internal static void OnDishReturnedToStorage(Sellable stored, long assignmentId)
    {
        try
        {
            if (stored == null || assignmentId <= 0
                || !_assignments.TryGetValue(assignmentId, out var assignment))
                return;

            assignment.Result = stored;
            string guid = ReadRuntimeGuid(stored);
            if (!string.IsNullOrEmpty(guid))
                assignment.ResultGuid = guid;
            assignment.StorageSignature = BuildStorageSignature(stored);
            assignment.InTray = false;
            assignment.TrayIndex = -1;
            assignment.InStorage = true;
            Plugin.Instance?.Log?.LogInfo(
                $"[MystiaRec] 稀客料理进入储藏区: 座位{assignment.DeskCode + 1} " +
                $"foodId={assignment.ExpectedFoodId}");
        }
        catch (Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 跟踪料理进入储藏区失败: " + e.Message);
        }
    }

    internal static void OnStorageExtractStarted(Sellable selected)
    {
        _storageExtractionAssignmentId = 0;
        if (selected == null) return;
        try
        {
            // 主路径只认玩家点中的具体储藏对象。若游戏把完全相同的料理合成一个
            // 堆叠，则绑定属于该明确堆叠，而不是在整个储藏区按先后顺序查找。
            var assignment = _assignments.Values
                .Where(a => a.Completed && a.InStorage)
                .FirstOrDefault(a => IsSameSellable(a.Result, selected));
            if (assignment == null)
            {
                string signature = BuildStorageSignature(selected);
                var stackMatches = _assignments.Values
                    .Where(a => a.Completed && a.InStorage
                        && string.Equals(a.StorageSignature, signature, StringComparison.Ordinal))
                    .ToList();
                // 只有唯一绑定落在这个精确料理堆叠时才兼容对象重建；有歧义就不猜。
                if (stackMatches.Count == 1)
                    assignment = stackMatches[0];
            }
            if (assignment != null)
                _storageExtractionAssignmentId = assignment.Id;
        }
        catch { }
    }

    internal static void OnStorageExtractFinished()
    {
        // 成功取出时 OnDishReceived 已消费该值；失败/托盘已满时在这里清掉。
        _storageExtractionAssignmentId = 0;
    }

    internal static void OnStoragePanelOpened()
    {
        _storagePanelOpen = true;
        _storageVisuals.Clear();
    }

    internal static void OnStoragePanelClosed()
    {
        _storagePanelOpen = false;
        _storageVisuals.Clear();
        _storageExtractionAssignmentId = 0;
    }

    internal static void OnStorageElementEnabled(object[] args)
    {
        try
        {
            if (!_storagePanelOpen || args == null || args.Length < 2) return;
            Sellable dish = ReadStorageEntryDish(args[0]);
            if (dish == null) return;

            Transform transform = null;
            for (int i = 1; i < args.Length && transform == null; i++)
            {
                if (args[i] is Component component)
                    transform = component.transform;
                else
                    transform = ReadTransform(args[i]);
            }
            if (transform == null) return;

            _storageVisuals[transform.GetInstanceID()] = new StorageDishVisual
            {
                Dish = dish,
                Transform = transform
            };
        }
        catch { }
    }

    internal static bool TryGetStorageTransform(RareDishAssignment assignment, out Transform transform)
    {
        transform = null;
        if (!_storagePanelOpen || assignment == null || !assignment.InStorage) return false;

        // 优先使用运行时唯一标识。对象因堆叠显示而被重建时，只允许唯一的完整
        // 料理特征匹配，避免储藏区中大量其他料理造成串号。
        var activeVisuals = _storageVisuals.Values
            .Where(v => IsActiveStorageVisual(v))
            .ToList();
        var exact = activeVisuals.FirstOrDefault(v => IsSameSellable(assignment.Result, v.Dish));
        if (exact != null)
        {
            transform = exact.Transform;
            return true;
        }

        if (string.IsNullOrEmpty(assignment.StorageSignature)) return false;
        var signatureMatches = activeVisuals
            .Where(v => string.Equals(BuildStorageSignature(v.Dish), assignment.StorageSignature,
                StringComparison.Ordinal))
            .ToList();
        if (signatureMatches.Count != 1) return false;
        transform = signatureMatches[0].Transform;
        return true;
    }

    internal static bool TryResolveTrayIndex(RareDishAssignment assignment, out int trayIndex)
    {
        trayIndex = -1;
        if (assignment == null || !assignment.InTray) return false;
        try
        {
            var elements = GameData.RunTime.NightSceneUtility.IzakayaTray.Instance?.Tray?.Elements;
            if (elements == null) return false;

            if (assignment.TrayIndex >= 0 && assignment.TrayIndex < elements.Length
                && IsSameSellable(assignment.Result, elements[assignment.TrayIndex]))
            {
                trayIndex = assignment.TrayIndex;
                return true;
            }

            for (int i = 0; i < elements.Length; i++)
            {
                if (!IsSameSellable(assignment.Result, elements[i])) continue;
                assignment.TrayIndex = i;
                trayIndex = i;
                return true;
            }

            // 料理已离开原格时立即隐藏残留编码。储藏补丁会在同一操作中把它
            // 切换成 InStorage；普通交付则由原有订单结束流程移除绑定。
            assignment.InTray = false;
            assignment.TrayIndex = -1;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 玩家可能在稀客点单前就做好料理。此时没有“开始制作”事件可用于自动选择方案，
    /// 因而在料理真正上桌时按该桌订单反向匹配料理和额外食材。
    /// </summary>
    internal static bool TryBindServedDish(
        int cardId,
        CustomerRecommendation card,
        Sellable servedFood)
    {
        try
        {
            if (card == null || servedFood == null
                || card.TrackingState != RecommendationTrackingState.AwaitingCook)
                return false;

            int bestQuality = 0;
            var indexes = new List<int>();
            for (int i = 0; i < card.Recommendations.Count; i++)
            {
                int quality = MatchServedRecommendation(card.Recommendations[i], servedFood);
                if (quality <= 0) continue;
                if (quality > bestQuality)
                {
                    bestQuality = quality;
                    indexes.Clear();
                }
                if (quality == bestQuality)
                    indexes.Add(i);
            }

            if (indexes.Count == 0) return false;
            if (!Plugin.TryLockBeverageForCook(cardId, card, indexes, out int selectedIndex))
                return false;

            card.MatchedRecommendationIndex = selectedIndex;
            card.TrackingState = RecommendationTrackingState.Completed;
            card.ActiveAssignmentId = 0;
            card.DragX = null;
            card.DragY = null;

            int foodId = -1;
            try { foodId = servedFood.Id; } catch { }
            string plan = selectedIndex >= 0 ? ((char)('A' + selectedIndex)).ToString() : "A/B同料理";
            Plugin.Instance?.Log?.LogInfo(
                $"[MystiaRec] 上桌料理反向绑定: 座位{card.DeskCode + 1} {card.CustomerName} " +
                $"foodId={foodId} 方案={plan}");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 上桌料理反向绑定失败: " + e.Message);
            return false;
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
        _storageVisuals.Clear();
        _storageExtractionAssignmentId = 0;
        _storagePanelOpen = false;
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

    private static int MatchServedRecommendation(Recommendation recommendation, Sellable servedFood)
    {
        if (recommendation == null || servedFood == null) return 0;

        int foodId = -1;
        try { foodId = servedFood.Id; } catch { }
        int expectedFoodId = recommendation.RecipeFoodId;
        if (expectedFoodId <= 0)
            expectedFoodId = RecipeDatabase.GetRecipe(recommendation.RecipeName)?.FoodId ?? -1;
        if (expectedFoodId != foodId) return 0;

        var expectedExtras = (recommendation.ExtraIngredients ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        var actualExtras = ReadModifierNames(servedFood, out bool reliable);

        if (expectedExtras.Count == 0)
            return actualExtras.Count == 0 ? 2 : 0;
        if (reliable)
            return expectedExtras.SequenceEqual(actualExtras, StringComparer.Ordinal) ? 2 : 0;
        return expectedExtras.Count == actualExtras.Count ? 1 : 0;
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

    private static string BuildStorageSignature(Sellable sellable)
    {
        if (sellable == null) return "";
        try
        {
            var modifierIds = new List<int>();
            var modifiers = sellable.Modifier;
            if (modifiers != null)
            {
                for (int i = 0; i < modifiers.Length; i++)
                    modifierIds.Add(modifiers[i]);
            }
            modifierIds.Sort();
            return $"{SafeReadFoodId(sellable)}|{sellable.Type}|{SafeReadAltered(sellable)}|" +
                string.Join(",", modifierIds);
        }
        catch
        {
            return SafeReadFoodId(sellable).ToString();
        }
    }

    private static int SafeReadFoodId(Sellable sellable)
    {
        try { return sellable?.Id ?? -1; }
        catch { return -1; }
    }

    private static Sellable ReadStorageEntryDish(object entry)
    {
        if (entry == null) return null;
        if (entry is Sellable sellable) return sellable;
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return entry.GetType().GetProperty("Key", flags)?.GetValue(entry) as Sellable;
        }
        catch { return null; }
    }

    private static Transform ReadTransform(object value)
    {
        if (value == null) return null;
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return value.GetType().GetProperty("transform", flags)?.GetValue(value) as Transform;
        }
        catch { return null; }
    }

    private static bool IsActiveStorageVisual(StorageDishVisual visual)
    {
        try
        {
            return visual?.Dish != null && visual.Transform != null
                && visual.Transform.gameObject.activeInHierarchy;
        }
        catch { return false; }
    }
}

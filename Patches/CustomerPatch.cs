using HarmonyLib;
using NightScene.GuestManagementUtility;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using MystiaRecommendation.Engine;

namespace MystiaRecommendation.Patches;

public static class CustomerPatch
{
    private static Dictionary<SpecialGuestsController, GuestState> _guestStates = new();
    private static System.Func<int, string> _getFoodTag;
    private static System.Func<int, string> _getBevTag;
    private static MethodInfo _containsServeInWorkMission;
    private static Dictionary<int, string> _deskGuests = new();
    private static float _nextFulfillmentPollTime;
    private const float FulfillmentPollInterval = 0.1f;

    private class GuestState
    {
        public string Name;
        public string LastFoodTag;
        public string LastBevTag;
        public string TextFoodTag;
        public string TextBevTag;
        public object LastOrder;
        public int LastBudget = -1;
        public int LastFixedRecipeId = -1;
        public int DeskCode = -1;
        public System.IntPtr LastOrderPointer = System.IntPtr.Zero;
        public long OrderVersion;
        public long RecommendedOrderVersion = -1;
        public long CompletedOrderVersion = -1;
    }

    static CustomerPatch()
    {
        try
        {
            var asm = typeof(SpecialGuestsController).Assembly;
            foreach (var type in asm.GetTypes())
            {
                if (type.Name == "DataBaseLanguage")
                {
                    var foodMethod = type.GetMethod("GetFoodTag", BindingFlags.Public | BindingFlags.Static);
                    if (foodMethod != null)
                        _getFoodTag = (id) => (string)foodMethod.Invoke(null, new object[] { id });

                    var bevMethod = type.GetMethod("GetBeverageTag", BindingFlags.Public | BindingFlags.Static);
                    if (bevMethod != null)
                        _getBevTag = (id) => (string)bevMethod.Invoke(null, new object[] { id });

                    Plugin.Instance?.Log.LogInfo("[MystiaRec] DataBaseLanguage FoodTag:" + (_getFoodTag != null) + " BevTag:" + (_getBevTag != null));
                    break;
                }
            }
        }
        catch { }
    }

    private static GuestState GetState(SpecialGuestsController sgc)
    {
        if (!_guestStates.ContainsKey(sgc))
            _guestStates[sgc] = new GuestState();
        return _guestStates[sgc];
    }

    private static int ReadDeskCode(SpecialGuestsController sgc)
    {
        try { return sgc.DeskCode; }
        catch { return -1; }
    }

    private static UnityEngine.Vector3? ReadGuestWorldPosition(SpecialGuestsController sgc)
    {
        try
        {
            if (sgc == null) return null;

            // Controller 是逻辑对象，不是 MonoBehaviour；实际场景位置在其客人实例上。
            var guests = sgc.guestInstances;
            if (guests == null) return null;
            for (int i = 0; i < guests.Length; i++)
            {
                var guest = guests[i];
                if (guest != null && guest.transform != null)
                    return guest.transform.position;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void ClearOrderState(GuestState state)
    {
        state.LastFoodTag = "";
        state.LastBevTag = "";
        state.TextFoodTag = "";
        state.TextBevTag = "";
        state.LastOrder = null;
        state.LastBudget = -1;
        state.LastFixedRecipeId = -1;
        state.LastOrderPointer = System.IntPtr.Zero;
        state.OrderVersion = 0;
        state.RecommendedOrderVersion = -1;
        state.CompletedOrderVersion = -1;
    }

    private static bool EnsureCurrentGuest(SpecialGuestsController sgc, string explicitName = null)
    {
        if (sgc == null) return false;

        var state = GetState(sgc);
        string name = explicitName;
        if (string.IsNullOrEmpty(name))
        {
            try { name = sgc.OnGetGuestName(); } catch { }
        }

        int deskCode = ReadDeskCode(sgc);
        if (string.IsNullOrEmpty(name))
        {
            state.DeskCode = deskCode;
            return false;
        }

        bool changedGuest = state.Name != name || state.DeskCode != deskCode;
        if (changedGuest)
        {
            ClearOrderState(state);
            state.Name = name;
            state.DeskCode = deskCode;
        }

        if (deskCode >= 0)
        {
            Plugin.ClearDeskIfOccupiedByOther(deskCode, name);
            if (_deskGuests.TryGetValue(deskCode, out var previousName) && previousName != name)
                Plugin.OnCustomerLeft(deskCode);
            _deskGuests[deskCode] = name;
        }

        return changedGuest;
    }

    [HarmonyPatch(typeof(SpecialGuestsController), "OnGetGuestName")]
    [HarmonyPostfix]
    public static void OnGetGuestName(string __result, SpecialGuestsController __instance)
    {
        try
        {
            if (string.IsNullOrEmpty(__result)) return;
            if (EnsureCurrentGuest(__instance, __result))
            {
                Plugin.Instance?.Log.LogInfo("[MystiaRec] Detected rare guest: " + __result);
                int currentDesk = ReadDeskCode(__instance);
                if (currentDesk >= 0)
                    Plugin.OnCustomerPending(__result, "", "", currentDesk, "waiting order",
                        ReadGuestWorldPosition(__instance));
                return;
            }
            if (!_guestStates.ContainsKey(__instance))
                _guestStates[__instance] = new GuestState();
            var state = _guestStates[__instance];
            if (state.Name != __result)
            {
                ClearOrderState(state);
                state.Name = __result;
                Plugin.Instance?.Log.LogInfo("[MystiaRec] 检测到稀客: " + __result);

                int deskIdx = -1;
                try { deskIdx = __instance.DeskCode; } catch { }
                if (deskIdx >= 0)
                    Plugin.OnCustomerPending(__result, "", "", deskIdx, "等待订单生成",
                        ReadGuestWorldPosition(__instance));
            }
        }
        catch { }
    }

    [HarmonyPatch(typeof(SpecialGuestsController), "PostGenerateOrder")]
    [HarmonyPostfix]
    public static void OnPostGenerateOrder(SpecialGuestsController __instance, object __result)
    {
        ResetOrderText(__instance);
        TryTriggerRecommend(__instance, "PostGenerateOrder", __result);
    }

    [HarmonyPatch(typeof(GuestGroupController), "PostGenerateOrder")]
    [HarmonyPostfix]
    public static void OnBasePostGenerateOrder(GuestGroupController __instance, object __result)
    {
        if (__instance is SpecialGuestsController sgc)
        {
            ResetOrderText(sgc);
            TryTriggerRecommend(sgc, "[Base]PostGenerateOrder", __result);
        }
        else
        {
            TryClearDeskForNormalGuest(__instance);
        }
    }

    [HarmonyPatch(typeof(SpecialGuestsController), "GetOrderFoodText")]
    [HarmonyPostfix]
    public static void OnGetOrderFoodText(string __result, SpecialGuestsController __instance, object __0)
    {
        TryUpdateTextTag(__instance, __result, false, __0, "GetOrderFoodText");
    }

    [HarmonyPatch(typeof(SpecialGuestsController), "GetOrderBevText")]
    [HarmonyPostfix]
    public static void OnGetOrderBevText(string __result, SpecialGuestsController __instance, object __0)
    {
        TryUpdateTextTag(__instance, __result, true, __0, "GetOrderBevText");
    }

    private static void TryTriggerRecommend(SpecialGuestsController sgc, string source, object orderData = null)
    {
        try
        {
            EnsureCurrentGuest(sgc);
            if (!_guestStates.ContainsKey(sgc))
                _guestStates[sgc] = new GuestState();
            var state = _guestStates[sgc];

            if (orderData != null)
            {
                var pointer = TryGetIl2CppPointer(orderData);
                bool changedOrder = pointer != System.IntPtr.Zero
                    ? pointer != state.LastOrderPointer
                    : !object.ReferenceEquals(orderData, state.LastOrder);
                if (changedOrder)
                {
                    state.OrderVersion++;
                    state.LastOrderPointer = pointer;
                    state.RecommendedOrderVersion = -1;
                    state.CompletedOrderVersion = -1;
                    state.LastFoodTag = "";
                    state.LastBevTag = "";
                    Plugin.Instance?.Log?.LogInfo($"[MystiaRec] 新订单对象: version={state.OrderVersion} ptr={pointer}");
                }
                state.LastOrder = orderData;
            }
            int orderBudget = TryReadBudgetFromObject(state.LastOrder);
            if (orderBudget > 0)
                state.LastBudget = orderBudget;

            string name = state.Name;
            if (string.IsNullOrEmpty(name))
            {
                try { name = sgc.OnGetGuestName(); state.Name = name; } catch { }
            }
            if (string.IsNullOrEmpty(name)) return;
            int fixedRecipeId = TryReadFixedRecipeIdFromMission(name, state.LastOrder);

            // 读取当前轮次的食物/酒水标签。订单文本回调最可靠，订单对象字段作为兜底。
            string reqFoodTag = state.TextFoodTag;
            string reqBevTag = state.TextBevTag;
            int deskIdx = -1;
            try { deskIdx = sgc.DeskCode; } catch { }

            if (string.IsNullOrEmpty(reqFoodTag))
                reqFoodTag = TryReadRequestTagFromSpecialOrder(state.LastOrder, false);
            if (string.IsNullOrEmpty(reqBevTag))
                reqBevTag = TryReadRequestTagFromSpecialOrder(state.LastOrder, true);
            if (string.IsNullOrEmpty(reqFoodTag))
                reqFoodTag = TryReadTagFromObject(state.LastOrder, false);
            if (string.IsNullOrEmpty(reqBevTag))
                reqBevTag = TryReadTagFromObject(state.LastOrder, true);
            if (string.IsNullOrEmpty(reqFoodTag))
                reqFoodTag = ResolveKnownTag(TryGetOrderText(sgc, state.LastOrder, "GetOrderFoodText"), false, name);
            if (string.IsNullOrEmpty(reqBevTag))
                reqBevTag = ResolveKnownTag(TryGetOrderText(sgc, state.LastOrder, "GetOrderBevText"), true, name);
            if (string.IsNullOrEmpty(reqFoodTag))
                reqFoodTag = state.TextFoodTag;
            if (string.IsNullOrEmpty(reqBevTag))
                reqBevTag = state.TextBevTag;

            try
            {
                if (string.IsNullOrEmpty(reqFoodTag))
                {
                    var likeFood = sgc.EvaluateLikeFoodTags;
                    if (likeFood != null && likeFood.Length > 0)
                    {
                        int tagId = likeFood[0];
                        reqFoodTag = _getFoodTag != null ? _getFoodTag(tagId) : tagId.ToString();
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(reqFoodTag) || string.IsNullOrEmpty(reqBevTag))
            {
                Plugin.Instance?.Log.LogInfo("[MystiaRec] 等待完整订单标签: 食物=" + reqFoodTag + ", 酒水=" + reqBevTag);
                if (deskIdx >= 0)
                    Plugin.OnCustomerPending(name, reqFoodTag, reqBevTag, deskIdx, "等待完整订单标签",
                        ReadGuestWorldPosition(sgc));
                return;
            }

            // 同一订单可能同时经过基类和派生类钩子；按订单对象去重，而不是按标签去重。
            // 这样连续两轮提出完全相同的标签也能正常刷新。
            if (state.CompletedOrderVersion == state.OrderVersion
                || (state.RecommendedOrderVersion == state.OrderVersion
                    && reqFoodTag == state.LastFoodTag
                    && reqBevTag == state.LastBevTag
                    && fixedRecipeId == state.LastFixedRecipeId))
                return;
            state.LastFoodTag = reqFoodTag;
            state.LastBevTag = reqBevTag;
            state.LastFixedRecipeId = fixedRecipeId;
            state.RecommendedOrderVersion = state.OrderVersion;

            Plugin.Instance?.Log.LogInfo("[MystiaRec] 稀客点单(" + source + "): " + name + " 座位" + deskIdx);
            Plugin.Instance?.Log.LogInfo("[MystiaRec] 食物标签: " + reqFoodTag + ", 酒水标签: " + reqBevTag);
            if (fixedRecipeId >= 0)
                Plugin.Instance?.Log.LogInfo("[MystiaRec] 检测到任务固定料理: foodId=" + fixedRecipeId);

            Plugin.OnCustomerArrived(name, reqFoodTag, reqBevTag, deskIdx, state.LastBudget, fixedRecipeId,
                ReadGuestWorldPosition(sgc), state.OrderVersion);
        }
        catch (System.Exception e)
        {
            Plugin.Instance?.Log.LogError("[MystiaRec] TryTriggerRecommend error: " + e.Message);
        }
    }

    public static void ResetAll()
    {
        _guestStates.Clear();
        _deskGuests.Clear();
        _nextFulfillmentPollTime = 0f;
    }

    /// <summary>
    /// 只检查本 Mod 已经跟踪到的稀客订单。
    ///
    /// 不再补丁 OrderBase 的上菜/上酒属性：这些属性由普通客人和稀客共用，
    /// 在 IL2CPP 下拦截它们会把普通订单也带进 Mod 的完成检测，存在原生崩溃风险。
    /// </summary>
    public static void PollFulfilledRareOrders()
    {
        if (UnityEngine.Time.unscaledTime < _nextFulfillmentPollTime) return;
        _nextFulfillmentPollTime = UnityEngine.Time.unscaledTime + FulfillmentPollInterval;

        if (Plugin.ActiveRecommendations.Count == 0 || _guestStates.Count == 0) return;

        foreach (var pair in _guestStates.ToList())
        {
            var state = pair.Value;
            if (state == null || state.LastOrder == null) continue;
            if (state.CompletedOrderVersion == state.OrderVersion) continue;
            if (state.RecommendedOrderVersion != state.OrderVersion) continue;
            if (!Plugin.ActiveRecommendations.Any(kv => kv.Value.DeskCode == state.DeskCode)) continue;

            try
            {
                if (state.LastOrder is not GuestsManager.OrderBase order) continue;
                if (order.ServFood == null || order.ServBeverage == null) continue;

                state.CompletedOrderVersion = state.OrderVersion;
                state.TextFoodTag = "";
                state.TextBevTag = "";
                Plugin.OnOrderFulfilled(state.DeskCode);
            }
            catch (System.Exception e)
            {
                Plugin.Instance?.Log?.LogWarning(
                    $"[MystiaRec] 稀客订单完成检测失败: 座位{state.DeskCode + 1} {e.Message}");
            }
        }
    }

    private static void ResetOrderText(SpecialGuestsController sgc)
    {
        try
        {
            if (sgc == null) return;
            if (!_guestStates.ContainsKey(sgc))
                _guestStates[sgc] = new GuestState();

            var state = _guestStates[sgc];
            state.TextFoodTag = "";
            state.TextBevTag = "";
        }
        catch { }
    }

    [HarmonyPatch(typeof(GuestsManager), "GuestPay")]
    [HarmonyPostfix]
    public static void OnGuestPay(GuestGroupController toPayAndLeave)
    {
        TryClearLeavingGuest(toPayAndLeave);
    }

    [HarmonyPatch(typeof(GuestsManager), "RemoveGuestIcon")]
    [HarmonyPostfix]
    public static void OnRemoveGuestIcon(GuestGroupController guestGroupController)
    {
        TryClearLeavingGuest(guestGroupController);
    }

    [HarmonyPatch(typeof(GuestsManager), "LeaveFromDesk")]
    [HarmonyPrefix]
    public static void OnLeaveFromDesk(GuestGroupController __0)
    {
        TryClearLeavingGuest(__0);
    }

    private static int _leaveLogCount = 0;

    private static System.IntPtr TryGetIl2CppPointer(object value)
    {
        try
        {
            if (value is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase il2CppObject)
                return il2CppObject.Pointer;
        }
        catch { }
        return System.IntPtr.Zero;
    }

    private static void TryClearLeavingGuest(GuestGroupController guest)
    {
        try
        {
            if (guest == null) return;

            var sgc = guest as SpecialGuestsController;
            bool isSpecial = sgc != null || guest.GetType().Name.Contains("SpecialGuests");

            _leaveLogCount++;
            if (_leaveLogCount <= 5)
                Plugin.Instance?.Log.LogInfo($"[MystiaRec] 离场钩子触发(#{_leaveLogCount}): type={guest.GetType().FullName}, isSpecial={isSpecial}");

            if (isSpecial)
            {
                int deskCode = -1;
                try { deskCode = guest.DeskCode; } catch { }
                Plugin.Instance?.Log.LogInfo($"[MystiaRec] 稀客离场: desk={deskCode}, 清理卡片");

                if (deskCode >= 0)
                {
                    Plugin.OnCustomerLeft(deskCode);
                    _deskGuests.Remove(deskCode);
                }

                if (sgc != null)
                    _guestStates.Remove(sgc);
            }
        }
        catch { }
    }

    private static void TryClearDeskForNormalGuest(GuestGroupController guest)
    {
        try
        {
            if (guest == null || guest is SpecialGuestsController) return;
            int deskCode = -1;
            try { deskCode = guest.DeskCode; } catch { }
            if (deskCode >= 0)
            {
                Plugin.ClearDeskIfOccupiedByOther(deskCode, "");
                _deskGuests.Remove(deskCode);
            }
        }
        catch { }
    }

    private static void TryUpdateTextTag(SpecialGuestsController sgc, string text, bool beverage, object orderData, string source)
    {
        try
        {
            if (sgc == null || string.IsNullOrWhiteSpace(text)) return;
            EnsureCurrentGuest(sgc);
            if (!_guestStates.ContainsKey(sgc))
                _guestStates[sgc] = new GuestState();

            var state = _guestStates[sgc];
            if (orderData != null)
                state.LastOrder = orderData;

            var tag = TryReadRequestTagFromSpecialOrder(orderData, beverage);
            if (string.IsNullOrEmpty(tag))
                tag = ResolveKnownTag(text, beverage, state.Name);
            if (string.IsNullOrEmpty(tag)) return;

            if (beverage)
                state.TextBevTag = tag;
            else
                state.TextFoodTag = tag;

            if (!string.IsNullOrEmpty(state.TextFoodTag) && !string.IsNullOrEmpty(state.TextBevTag))
                TryTriggerRecommend(sgc, source, state.LastOrder);
        }
        catch { }
    }

    private static int TryReadBudgetFromObject(object source)
    {
        if (source == null) return -1;
        var seen = new HashSet<object>();
        return TryReadBudgetFromObject(source, seen, 0);
    }

    private static int TryReadBudgetFromObject(object source, HashSet<object> seen, int depth)
    {
        if (source == null || depth > 2 || seen.Contains(source)) return -1;
        seen.Add(source);

        var type = source.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            var value = SafeGet(() => prop.GetValue(source));
            int budget = TryReadBudgetValue(prop.Name, value, seen, depth);
            if (budget > 0) return budget;
        }

        foreach (var field in type.GetFields(flags))
        {
            var value = SafeGet(() => field.GetValue(source));
            int budget = TryReadBudgetValue(field.Name, value, seen, depth);
            if (budget > 0) return budget;
        }

        return -1;
    }

    private static int TryReadBudgetValue(string name, object value, HashSet<object> seen, int depth)
    {
        if (value == null) return -1;

        var lower = name.ToLowerInvariant();
        bool looksLikeBudget = lower.Contains("money") || lower.Contains("budget") || lower.Contains("remain") || lower.Contains("price") || lower.Contains("max");
        if (looksLikeBudget && int.TryParse(value.ToString(), out int number) && number > 0 && number < 100000)
            return number;

        if (!value.GetType().IsPrimitive && value is not string)
            return TryReadBudgetFromObject(value, seen, depth + 1);

        return -1;
    }

    private static string TryGetOrderText(SpecialGuestsController sgc, object orderData, string methodName)
    {
        if (sgc == null || orderData == null) return "";

        try
        {
            var method = sgc.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return "";

            var value = method.Invoke(sgc, new[] { orderData });
            return value?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string TryReadTagFromObject(object source, bool beverage)
    {
        if (source == null) return "";
        return TryReadTagFromObject(source, beverage, new HashSet<object>(), 0);
    }

    private static string TryReadRequestTagFromSpecialOrder(object orderData, bool beverage)
    {
        if (orderData == null) return "";

        try
        {
            var type = orderData.GetType();
            string propertyName = beverage ? "RequestBeverageTag" : "RequestFoodTag";
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            var value = property?.GetValue(orderData);
            if (TryResolveTagId(value, beverage, out var tag))
                return tag;

            string getterName = beverage ? "get_RequestBeverageTag" : "get_RequestFoodTag";
            var getter = type.GetMethod(getterName, BindingFlags.Public | BindingFlags.Instance);
            value = getter?.Invoke(orderData, null);
            if (TryResolveTagId(value, beverage, out tag))
                return tag;
        }
        catch { }

        return "";
    }

    /// <summary>
    /// 营业任务存在时，游戏的 ContainsSpecialNPCServeInWorkMission 会返回固定 food ID。
    /// SpecialOrder.RequestFood 仅用作接口返回异常时的同任务兜底，不能单独作为任务判据。
    /// </summary>
    private static int TryReadFixedRecipeIdFromMission(string customerName, object orderData)
    {
        if (string.IsNullOrEmpty(customerName)) return -1;

        try
        {
            var customer = Plugin.DataEngine?.GetCustomer(customerName);
            if (customer == null) return -1;

            if (_containsServeInWorkMission == null)
            {
                var asm = typeof(SpecialGuestsController).Assembly;
                _containsServeInWorkMission = asm.GetTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    .FirstOrDefault(m => m.Name == "ContainsSpecialNPCServeInWorkMission"
                        && m.GetParameters().Length == 2);
                Plugin.Instance?.Log?.LogInfo("[MystiaRec] 营业任务接口: " +
                    (_containsServeInWorkMission != null ? _containsServeInWorkMission.DeclaringType?.FullName : "未找到"));
            }

            if (_containsServeInWorkMission == null) return -1;

            object[] args = { customer.id, -1 };
            bool hasMission = System.Convert.ToBoolean(_containsServeInWorkMission.Invoke(null, args));
            if (!hasMission) return -1;

            int missionFoodId = System.Convert.ToInt32(args[1]);
            if (missionFoodId >= 0) return missionFoodId;

            // 理论上任务接口会直接给出 food ID；仅在异常返回时读取同一任务订单中的 RequestFood。
            if (orderData == null) return -1;
            var type = orderData.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var requestFood = type.GetProperty("RequestFood", flags)?.GetValue(orderData)
                ?? type.GetMethod("get_RequestFood", flags)?.Invoke(orderData, null);
            if (requestFood == null) return -1;

            var foodType = requestFood.GetType();
            foreach (var memberName in new[] { "id", "Id", "ID", "FoodId", "FoodID" })
            {
                var value = foodType.GetProperty(memberName, flags)?.GetValue(requestFood)
                    ?? foodType.GetField(memberName, flags)?.GetValue(requestFood);
                if (value != null && int.TryParse(value.ToString(), out int id) && id >= 0)
                    return id;
            }
        }
        catch (System.Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 读取任务固定料理失败: " + e.Message);
        }

        return -1;
    }

    private static bool TryResolveTagId(object value, bool beverage, out string tag)
    {
        tag = "";
        if (value == null) return false;

        try
        {
            int id = System.Convert.ToInt32(value);
            if (id < 0) return false;
            tag = beverage ? (_getBevTag?.Invoke(id) ?? id.ToString()) : (_getFoodTag?.Invoke(id) ?? id.ToString());
            return !string.IsNullOrEmpty(tag);
        }
        catch
        {
            return false;
        }
    }

    private static string TryReadTagFromObject(object source, bool beverage, HashSet<object> seen, int depth)
    {
        if (source == null || depth > 2 || seen.Contains(source)) return "";
        seen.Add(source);

        var type = source.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            var tag = TryReadMemberTag(prop.Name, SafeGet(() => prop.GetValue(source)), beverage, seen, depth);
            if (!string.IsNullOrEmpty(tag)) return tag;
        }

        foreach (var field in type.GetFields(flags))
        {
            var tag = TryReadMemberTag(field.Name, SafeGet(() => field.GetValue(source)), beverage, seen, depth);
            if (!string.IsNullOrEmpty(tag)) return tag;
        }

        return "";
    }

    private static string TryReadMemberTag(string name, object value, bool beverage, HashSet<object> seen, int depth)
    {
        if (value == null) return "";

        var lower = name.ToLowerInvariant();
        bool looksLikeTarget = beverage
            ? (lower.Contains("bev") || lower.Contains("beverage") || lower.Contains("drink"))
            : lower.Contains("food");
        bool looksLikeTag = lower.Contains("tag");

        if (looksLikeTarget && looksLikeTag)
        {
            if (value is int id)
                return beverage ? (_getBevTag?.Invoke(id) ?? id.ToString()) : (_getFoodTag?.Invoke(id) ?? id.ToString());

            if (value is System.Collections.IEnumerable items && value is not string)
            {
                foreach (var item in items)
                {
                    if (item is int itemId)
                        return beverage ? (_getBevTag?.Invoke(itemId) ?? itemId.ToString()) : (_getFoodTag?.Invoke(itemId) ?? itemId.ToString());
                }
            }
        }

        if (looksLikeTarget && value is string text)
            return ResolveKnownTag(text, beverage);

        if (looksLikeTarget && !value.GetType().IsPrimitive && value is not string)
            return TryReadTagFromObject(value, beverage, seen, depth + 1);

        return "";
    }

    private static T SafeGet<T>(System.Func<T> getter)
    {
        try { return getter(); }
        catch { return default; }
    }

    private static string ResolveKnownTag(string text, bool beverage, string customerName = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var tags = beverage
            ? RecipeDatabase.GetAllBeverages().SelectMany(b => b.Tags)
            : RecipeDatabase.GetAllRecipes()
                .SelectMany(r => r.PositiveTags.Concat(r.NegativeTags))
                .Concat(Plugin.DataEngine?.GetAllCustomers().Values.SelectMany(c => c.positiveTags.Concat(c.negativeTags)) ?? Enumerable.Empty<string>());

        var knownTags = tags
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();

        var mappedTag = ResolveMappedTag(text, beverage, customerName, knownTags);
        if (!string.IsNullOrEmpty(mappedTag))
            return mappedTag;

        foreach (var tag in knownTags.OrderByDescending(t => t.Length))
            if (text.Contains(tag))
                return tag;

        return "";
    }

    private static string ResolveMappedTag(string text, bool beverage, string customerName, List<string> knownTags)
    {
        if (string.IsNullOrWhiteSpace(customerName)) return "";
        var customer = Plugin.DataEngine?.GetCustomer(customerName);
        if (customer == null) return "";

        var mappings = beverage ? customer.beverageTagMapping : customer.positiveTagMapping;
        var allowedTags = beverage ? customer.beverageTags : customer.positiveTags;
        if (mappings == null || mappings.Count == 0 || allowedTags == null) return "";

        foreach (var kv in mappings.OrderByDescending(kv => kv.Value?.Length ?? 0))
        {
            var tag = kv.Key;
            var phrase = kv.Value;
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(phrase)) continue;
            if (!knownTags.Contains(tag) || !allowedTags.Contains(tag)) continue;
            if (text.Contains(phrase))
                return tag;
        }

        return "";
    }
}

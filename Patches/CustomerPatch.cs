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
    private static Dictionary<int, string> _deskGuests = new();

    private class GuestState
    {
        public string Name;
        public string LastFoodTag;
        public string LastBevTag;
        public string TextFoodTag;
        public string TextBevTag;
        public object LastOrder;
        public int LastBudget = -1;
        public int DeskCode = -1;
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

    private static void ClearOrderState(GuestState state)
    {
        state.LastFoodTag = "";
        state.LastBevTag = "";
        state.TextFoodTag = "";
        state.TextBevTag = "";
        state.LastOrder = null;
        state.LastBudget = -1;
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
                    Plugin.OnCustomerPending(__result, "", "", currentDesk, "waiting order");
                return;
            }
            if (!_guestStates.ContainsKey(__instance))
                _guestStates[__instance] = new GuestState();
            var state = _guestStates[__instance];
            if (state.Name != __result)
            {
                state.Name = __result;
                state.LastFoodTag = "";
                state.LastBevTag = "";
                state.TextFoodTag = "";
                state.TextBevTag = "";
                state.LastOrder = null;
                Plugin.Instance?.Log.LogInfo("[MystiaRec] 检测到稀客: " + __result);

                int deskIdx = -1;
                try { deskIdx = __instance.DeskCode; } catch { }
                if (deskIdx >= 0)
                    Plugin.OnCustomerPending(__result, "", "", deskIdx, "等待订单生成");
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
            state.LastOrder = orderData ?? state.LastOrder;
            int orderBudget = TryReadBudgetFromObject(state.LastOrder);
            if (orderBudget > 0)
                state.LastBudget = orderBudget;

            string name = state.Name;
            if (string.IsNullOrEmpty(name))
            {
                try { name = sgc.OnGetGuestName(); state.Name = name; } catch { }
            }
            if (string.IsNullOrEmpty(name)) return;

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
                    Plugin.OnCustomerPending(name, reqFoodTag, reqBevTag, deskIdx, "等待完整订单标签");
                return;
            }

            // 多轮点单：只在标签变化时触发新推荐
            if (reqFoodTag == state.LastFoodTag && reqBevTag == state.LastBevTag)
                return;
            state.LastFoodTag = reqFoodTag;
            state.LastBevTag = reqBevTag;

            Plugin.Instance?.Log.LogInfo("[MystiaRec] 稀客点单(" + source + "): " + name + " 座位" + deskIdx);
            Plugin.Instance?.Log.LogInfo("[MystiaRec] 食物标签: " + reqFoodTag + ", 酒水标签: " + reqBevTag);

            Plugin.OnCustomerArrived(name, reqFoodTag, reqBevTag, deskIdx, state.LastBudget);
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

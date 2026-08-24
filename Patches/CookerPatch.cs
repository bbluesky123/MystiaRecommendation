using GameData.Core.Collections;
using HarmonyLib;
using MystiaRecommendation.Engine;
using NightScene.CookingUtility;
using GameData.RunTime.NightSceneUtility;

namespace MystiaRecommendation.Patches;

/// <summary>
/// 料理一旦开始后只观察最终结果：正常料理保留座位绑定，失败料理恢复完整推荐。
/// </summary>
public static class CookerPatch
{
    [HarmonyPatch(typeof(CookController), "SetCook")]
    [HarmonyPostfix]
    public static void OnSetCook(CookController __instance, Sellable thisResult, Recipe recipe)
    {
        RuntimeOrderTracker.OnCookStarted(__instance, thisResult, recipe);
    }

    [HarmonyPatch(typeof(CookController), "FinishCooking")]
    [HarmonyPostfix]
    public static void OnFinishCooking(CookController __instance)
    {
        RuntimeOrderTracker.OnCookFinished(__instance);
    }

    [HarmonyPatch(typeof(CookController), "GetFinalFood")]
    [HarmonyPostfix]
    public static void OnGetFinalFood(CookController __instance, Sellable __result)
    {
        RuntimeOrderTracker.OnFinalFoodResolved(__instance, __result);
    }

    [HarmonyPatch(typeof(CookController), "AfterPlayerExtract")]
    [HarmonyPostfix]
    public static void OnAfterPlayerExtract(CookController __instance)
    {
        RuntimeOrderTracker.OnDishExtracted(__instance);
    }

    [HarmonyPatch(typeof(IzakayaTray), "Receive")]
    [HarmonyPostfix]
    public static void OnTrayReceive(Sellable value, int __result)
    {
        RuntimeOrderTracker.OnDishReceived(value, __result);
    }

    [HarmonyPatch(typeof(IzakayaTray), "RecieveInternal")]
    [HarmonyPostfix]
    public static void OnTrayReceiveInternal(Sellable value, int __result)
    {
        RuntimeOrderTracker.OnDishReceived(value, __result);
    }

    [HarmonyPatch(typeof(IzakayaTray), "Deliver")]
    [HarmonyPostfix]
    public static void OnTrayDeliver(Sellable value)
    {
        RuntimeOrderTracker.OnDishDelivered(value);
    }

    [HarmonyPatch(typeof(IzakayaTray), "DeliverInternal")]
    [HarmonyPostfix]
    public static void OnTrayDeliverInternal(Sellable value)
    {
        RuntimeOrderTracker.OnDishDelivered(value);
    }
}

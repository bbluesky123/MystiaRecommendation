using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace MystiaRecommendation.UI;

/// <summary>
/// MonoBehaviour 组件，用于 Hook Unity 的 OnGUI 回调
/// </summary>
public class GUIBehaviour : MonoBehaviour
{
    private static GUIBehaviour _instance;
    private static OverlayRenderer _overlayRenderer;
    private int _frameCount = 0;
    private string _lastSceneName = "";
    private float _lastLevelTime = -1f;

    // IL2CPP 需要的构造函数
    public GUIBehaviour(System.IntPtr ptr) : base(ptr) { }

    /// <summary>
    /// 创建 GUIBehaviour
    /// </summary>
    public static void Create()
    {
        if (_instance != null) return;

        _overlayRenderer = new OverlayRenderer();

        // 先注册类型到 IL2CPP 域
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(GUIBehaviour)))
        {
            ClassInjector.RegisterTypeInIl2Cpp<GUIBehaviour>();
        }

        // 创建 GameObject 并添加组件
        var go = new GameObject("MystiaRecommendation_GUI");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        _instance = go.AddComponent<GUIBehaviour>();
    }

    public void OnGUI()
    {
        try
        {
            _frameCount++;
            if (_frameCount % 300 == 1)
            {
                int activeCount = Plugin.ActiveRecommendations?.Count ?? 0;
                bool showOverlay = Plugin.PluginConfig?.ShowOverlay?.Value ?? false;
                Plugin.Instance?.Log?.LogDebug($"[MystiaRec] OnGUI frame={_frameCount} active={activeCount} showOverlay={showOverlay}");
            }

            if (_overlayRenderer != null && Plugin.PluginConfig.ShowOverlay.Value)
            {
                _overlayRenderer.Draw();
            }
        }
        catch (System.Exception e)
        {
            Plugin.Instance?.Log?.LogError("[MystiaRec] OnGUI error: " + e.Message);
        }
    }

    public void Update()
    {
        try
        {
            CheckSceneChanged();
            PeriodicHealthCheck();

            if (Input.GetKeyDown(Plugin.PluginConfig.ToggleKey.Value))
            {
                Plugin.PluginConfig.ShowOverlay.Value = !Plugin.PluginConfig.ShowOverlay.Value;
                Plugin.Instance?.Log?.LogInfo("[MystiaRec] 叠加显示: " + Plugin.PluginConfig.ShowOverlay.Value);
            }

            if (Input.GetKeyDown(Plugin.PluginConfig.RefreshKey.Value))
            {
                Plugin.Instance?.Log?.LogInfo("[MystiaRec] 手动刷新推荐");
                Plugin.RefreshActiveRecommendations();
            }
        }
        catch { }
    }

    private float _lastHealthCheckTime = 0f;
    private const float HEALTH_CHECK_INTERVAL = 10f;

    /// <summary>
    /// 定期健康检查 / 营业结束大保底：
    /// 检测游戏内是否还有活跃客人，如果没有则清空所有卡片。
    /// </summary>
    private void PeriodicHealthCheck()
    {
        if (Time.time - _lastHealthCheckTime < HEALTH_CHECK_INTERVAL) return;
        _lastHealthCheckTime = Time.time;

        if (Plugin.ActiveRecommendations.Count == 0) return;

        try
        {
            int activeGuestCount = GetActiveGuestCount();
            // 大保底：活跃客人为0时清空所有卡片（营业已结束或客人已全部离场）
            if (activeGuestCount == 0)
            {
                Plugin.Instance?.Log?.LogInfo("[MystiaRec] 健康检查: 已无活跃客人，清空所有卡片（营业结束大保底）");
                Plugin.ClearAllRecommendations();
                Patches.CustomerPatch.ResetAll();
            }
        }
        catch (System.Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 健康检查异常: " + e.Message);
        }
    }

    /// <summary>
    /// 尝试获取当前游戏内的活跃客人数量。通过 GuestsManager 获取。
    /// </summary>
    private static int GetActiveGuestCount()
    {
        try
        {
            var asm = typeof(NightScene.GuestManagementUtility.SpecialGuestsController).Assembly;
            var managerType = asm.GetTypes().FirstOrDefault(t => t.Name == "GuestsManager");
            if (managerType == null) return -1;

            var instanceProp = managerType.GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var instance = instanceProp?.GetValue(null);
            if (instance == null) return -1;

            // 尝试读取 Guests 或 GuestGroupList
            var guests = ReadEnumerableProperty(instance, "Guests", "GuestList", "ActiveGuests");
            if (guests == null) return -1;

            int count = 0;
            foreach (var _ in guests) count++;
            return count;
        }
        catch { return -1; }
    }

    /// <summary>
    /// 获取当前被客人占用的座位号集合
    /// </summary>
    private static HashSet<int> GetOccupiedDeskCodes()
    {
        var result = new HashSet<int>();
        try
        {
            var asm = typeof(NightScene.GuestManagementUtility.SpecialGuestsController).Assembly;
            var managerType = asm.GetTypes().FirstOrDefault(t => t.Name == "GuestsManager");
            if (managerType == null) return result;

            var instanceProp = managerType.GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var instance = instanceProp?.GetValue(null);
            if (instance == null) return result;

            var guests = ReadEnumerableProperty(instance, "Guests", "GuestList", "ActiveGuests");
            if (guests == null) return result;

            foreach (var guest in guests)
            {
                try
                {
                    int desk = (int)guest.GetType().GetProperty("DeskCode")?.GetValue(guest);
                    if (desk >= 0) result.Add(desk);
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    private static System.Collections.IEnumerable ReadEnumerableProperty(object obj, params string[] names)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        foreach (var name in names)
        {
            try
            {
                var prop = type.GetProperty(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                    return prop.GetValue(obj) as System.Collections.IEnumerable;
            }
            catch { }
        }
        return null;
    }

    private void CheckSceneChanged()
    {
        string sceneName = "";
        try
        {
            sceneName = Application.loadedLevelName ?? "";
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(sceneName))
            return;

        float levelTime = Time.timeSinceLevelLoad;

        if (string.IsNullOrEmpty(_lastSceneName))
        {
            _lastSceneName = sceneName;
            _lastLevelTime = levelTime;
            return;
        }

        bool sceneChanged = _lastSceneName != sceneName;
        bool sameSceneReloaded = _lastLevelTime >= 0f && levelTime + 1f < _lastLevelTime;
        _lastLevelTime = levelTime;

        if (!sceneChanged && !sameSceneReloaded)
            return;

        Plugin.ClearAllRecommendations();
        Patches.CustomerPatch.ResetAll();
        Plugin.Instance?.Log?.LogInfo($"[MystiaRec] 场景刷新，清理稀客推荐: {_lastSceneName} -> {sceneName}");
        _lastSceneName = sceneName;
    }

    public static void Destroy()
    {
        if (_instance != null)
        {
            Object.Destroy(_instance.gameObject);
            _instance = null;
        }
    }
}

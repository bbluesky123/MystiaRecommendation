using BepInEx.Configuration;

namespace MystiaRecommendation.Config;

public class PluginConfig
{
    public ConfigEntry<bool> ShowOverlay { get; }
    public ConfigEntry<string> Position { get; }
    public ConfigEntry<float> Opacity { get; }
    public ConfigEntry<int> FontSize { get; }
    public ConfigEntry<float> AutoHideDelay { get; }
    public ConfigEntry<bool> ConsiderPopularTrend { get; }
    public ConfigEntry<int> MaxExtraIngredients { get; }
    public ConfigEntry<bool> PreferNightingaleCooker { get; }
    public ConfigEntry<bool> ShowTags { get; }
    public ConfigEntry<bool> ShowSpellCards { get; }
    public ConfigEntry<bool> ShowBondRewards { get; }
    public ConfigEntry<UnityEngine.KeyCode> ToggleKey { get; }
    public ConfigEntry<UnityEngine.KeyCode> RefreshKey { get; }

    public PluginConfig(ConfigFile cfg)
    {
        ShowOverlay = cfg.Bind("显示设置", "启用叠加显示", true, "总开关");
        Position = cfg.Bind("显示设置", "显示位置", "OverCustomer", "OverCustomer / TopLeft / RightSide");
        Opacity = cfg.Bind("显示设置", "透明度", 0.85f,
            new ConfigDescription("透明度", new AcceptableValueRange<float>(0f, 1f)));
        FontSize = cfg.Bind("显示设置", "字体大小", 14,
            new ConfigDescription("字体大小", new AcceptableValueRange<int>(10, 24)));
        AutoHideDelay = cfg.Bind("显示设置", "自动隐藏延迟秒", 10f,
            new ConfigDescription("延迟", new AcceptableValueRange<float>(0f, 60f)));
        ConsiderPopularTrend = cfg.Bind("推荐策略", "考虑流行趋势", true);
        MaxExtraIngredients = cfg.Bind("推荐策略", "最大额外食材数", 3,
            new ConfigDescription("最大额外食材", new AcceptableValueRange<int>(0, 5)));
        PreferNightingaleCooker = cfg.Bind("厨具设置", "优先夜雀厨具", true);
        ShowTags = cfg.Bind("辅助功能", "显示标签", true);
        ShowSpellCards = cfg.Bind("辅助功能", "显示符卡", true);
        ShowBondRewards = cfg.Bind("辅助功能", "显示羁绊", true);
        ToggleKey = cfg.Bind("热键", "切换显示", UnityEngine.KeyCode.F2);
        RefreshKey = cfg.Bind("热键", "刷新", UnityEngine.KeyCode.F5);
    }
}

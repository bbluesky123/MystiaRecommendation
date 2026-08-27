using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MystiaRecommendation.Engine;
using NightScene.UI;

namespace MystiaRecommendation.UI;

/// <summary>
/// 多稀客推荐叠加渲染器 - 可拖拽+折叠卡片
/// </summary>
public class OverlayRenderer
{
    private bool _stylesInitialized;
    private Texture2D _bgCard;
    private Texture2D _badgeBorderBg;
    private Texture2D _badgeCookingBg;
    private Texture2D _badgeCompletedBg;
    private float _bgOpacity = -1f;

    // 样式
    private GUIStyle _titleStyle;
    private GUIStyle _deskStyle;
    private GUIStyle _tagPosStyle;
    private GUIStyle _tagNegStyle;
    private GUIStyle _tagBevStyle;
    private GUIStyle _tagReqStyle;
    private GUIStyle _tagBevPrefStyle;
    private GUIStyle _detailStyle;
    private GUIStyle _ratingPerfectStyle;
    private GUIStyle _ratingGoodStyle;
    private GUIStyle _ratingOkStyle;
    private GUIStyle _sectionToggleStyle;
    private GUIStyle _sectionHeaderStyle;
    private GUIStyle _scorePosStyle;
    private GUIStyle _scoreZeroStyle;
    private GUIStyle _dragHandleStyle;
    private GUIStyle _recipeNameStyle;
    private GUIStyle _beverageNameStyle;
    private GUIStyle _ingredientStyle;
    private GUIStyle _likeLineStyle;
    private GUIStyle _hateLineStyle;
    private GUIStyle _planTitleStyle;
    private GUIStyle _compactStyle;
    private GUIStyle _pendingInteractionStyle;
    private GUIStyle _pendingWaitingStyle;
    private GUIStyle _taskWarningStyle;
    private GUIStyle _badgeShadowStyle;
    private GUIStyle _badgeStyle;

    // 布局常量
    private const float CARD_WIDTH = 360;
    private const float COMPACT_CARD_WIDTH = 180;
    private const float CARD_PADDING = 8;
    private const float CARD_SPACING = 10;
    private const float SCREEN_MARGIN = 10;
    private const int MAX_RECIPES = 2;
    // 行高
    private const float LINE_HEIGHT = 22;
    private const float TAG_LINE_HEIGHT = 20;

    // 拖拽状态
    private int _draggedCardId = -1;
    private float _dragStartMouseX;
    private float _dragStartMouseY;
    private float _dragStartCardX;
    private float _dragStartCardY;
    private bool _eventConsumed;

    // 原始输入状态（绕过 IMGUI Event.current，用 Input 类直接读取）
    private float _inputMouseX;
    private float _inputMouseY;
    private bool _inputMouseDown;
    private bool _inputMouseUp;
    private bool _inputMouseHeld;
    private int _lastInputFrame;
    private static int _deskPointCacheFrame = -1000;
    private static Dictionary<int, Vector2> _deskPointCache = new();
    private static WorkSceneTrayPannel _trayPanelCache;
    private static int _trayPanelLookupFrame = -1000;

    // Z序：最近拖拽的ID排最后
    private List<int> _dragOrder = new();

    public void Draw()
    {
        if (Plugin.ActiveRecommendations.Count == 0) return;
        if (!Plugin.PluginConfig.ShowOverlay.Value) return;

        float opacity = Plugin.PluginConfig.Opacity.Value;
        if (!_stylesInitialized || System.Math.Abs(_bgOpacity - opacity) > 0.001f)
        {
            InitStyles(Plugin.PluginConfig.FontSize.Value, opacity);
            _stylesInitialized = true;
        }

        // 先拍快照，避免在 OnGUI 多遍调用期间集合被修改
        var allCards = Plugin.ActiveRecommendations.ToList();

        var active = allCards
            .Where(kv => !kv.Value.IsFadingOut || kv.Value.FadeAlpha > 0)
            .OrderBy(kv => kv.Key)
            .Take(12)
            .ToList();

        if (active.Count == 0) return;

        // 淡出
        foreach (var kv in active)
        {
            if (kv.Value.IsFadingOut)
                kv.Value.FadeAlpha -= Time.deltaTime / Plugin.PluginConfig.AutoHideDelay.Value;
        }

        // 清理已完全淡出的卡片（基于快照遍历，避免直接枚举字典）
        var toRemove = allCards
            .Where(kv => kv.Value.IsFadingOut && kv.Value.FadeAlpha <= 0)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in toRemove)
        {
            RuntimeOrderTracker.RemoveForCard(key);
            Plugin.ReleaseBeverageReservation(key);
            Plugin.ActiveRecommendations.Remove(key);
            _dragOrder.Remove(key);
            if (_draggedCardId == key) _draggedCardId = -1;
        }

        // 读取原始鼠标输入（绕过 IMGUI Event.current，解决游戏内其他UI消费事件的问题）
        CaptureInput();

        _eventConsumed = false;

        // 处理拖拽中的 MouseDrag / MouseUp
        ProcessDragEvents();

        // 完整方案进入右上角决策区；确认方案和等待提示进入桌边区。
        var autoCards = active.Where(kv => !kv.Value.DragX.HasValue).ToList();
        var draggedCards = active.Where(kv => kv.Value.DragX.HasValue).ToList();
        var decisionCards = autoCards.Where(kv => IsDecisionCard(kv.Value)).ToList();
        var localCards = autoCards.Where(kv => !IsDecisionCard(kv.Value)).ToList();

        DrawDecisionCards(decisionCards);
        DrawLocalCards(localCards);

        // === 阶段2：绘制拖拽卡片（在列布局之上，按Z序） ===
        foreach (var cardId in _dragOrder.ToList())
        {
            var kv = draggedCards.FirstOrDefault(k => k.Key == cardId);
            if (kv.Key == 0 && kv.Value == null) continue;
            if (!kv.Value.DragX.HasValue) continue;
            float h = CalcCardHeight(kv.Value);
            float x = ClampCardX(kv.Value.DragX.Value, GetCardWidth(kv.Value));
            float y = ClampCardY(kv.Value.DragY ?? SCREEN_MARGIN, h);
            kv.Value.DragX = x;
            kv.Value.DragY = y;
            DrawCard(x, y, kv.Value, h, kv.Key);
        }

        // 清理不在活跃列表中的拖拽排序
        var activeIds = new HashSet<int>(active.Select(k => k.Key));
        _dragOrder.RemoveAll(id => !activeIds.Contains(id));

        DrawDishBadges();
    }

    private void DrawDecisionCards(List<KeyValuePair<int, CustomerRecommendation>> cards)
    {
        float y = SCREEN_MARGIN;
        int column = 0;
        float bottom = Screen.height - SCREEN_MARGIN;

        foreach (var kv in cards)
        {
            float height = CalcCardHeight(kv.Value);
            if (y > SCREEN_MARGIN && y + height > bottom)
            {
                column++;
                y = SCREEN_MARGIN;
            }

            // 第一列贴右上角；放满后向左开启下一列。
            float x = Screen.width - SCREEN_MARGIN - CARD_WIDTH - column * (CARD_WIDTH + 12);
            x = ClampCardX(x, CARD_WIDTH);
            DrawCard(x, y, kv.Value, height, kv.Key);
            y += height + CARD_SPACING;
        }
    }

    private void DrawLocalCards(List<KeyValuePair<int, CustomerRecommendation>> cards)
    {
        var occupied = new List<Rect>();
        DrawLocalSeatColumns(cards, occupied);
    }

    /// <summary>
    /// 所有桌边提示按桌号固定分栏：5-8 号桌统一放左侧，1-4 号桌统一放右侧。
    /// “请对话/正在获取需求/等待下一轮”和已确认订单使用同一规则，只认桌号牌，
    /// 人物位置不参与桌边卡片定位。
    /// </summary>
    private void DrawLocalSeatColumns(
        List<KeyValuePair<int, CustomerRecommendation>> cards,
        List<Rect> occupied)
    {
        var deskPoints = GetDeskScreenPoints();
        var anchored = cards
            .Select(kv =>
            {
                bool hasPoint = deskPoints.TryGetValue(kv.Value.DeskCode, out var point);
                return (Card: kv, HasPoint: hasPoint, Point: point);
            })
            .Where(x => x.HasPoint)
            .ToList();

        if (anchored.Count == 0) return;

        var positioned = anchored
            .Select(x =>
            {
                bool placeRight = IsRightDeskGroup(x.Card.Value.DeskCode);
                return (x.Card, x.Point, PlaceRight: placeRight);
            })
            .ToList();

        const float seatGap = 34f;
        float leftColumnX = positioned.Any(x => !x.PlaceRight)
            ? positioned.Where(x => !x.PlaceRight).Average(x => x.Point.x)
                - COMPACT_CARD_WIDTH - seatGap
            : SCREEN_MARGIN;
        float rightColumnX = positioned.Any(x => x.PlaceRight)
            ? positioned.Where(x => x.PlaceRight).Average(x => x.Point.x) + seatGap
            : Screen.width - SCREEN_MARGIN - COMPACT_CARD_WIDTH;

        foreach (var entry in positioned.OrderBy(x => x.Point.y).ThenBy(x => x.Card.Value.DeskCode))
        {
            float height = CalcCardHeight(entry.Card.Value);
            float x = entry.PlaceRight ? rightColumnX : leftColumnX;
            float y = entry.Point.y - height * 0.5f;
            var rect = new Rect(
                ClampCardX(x, COMPACT_CARD_WIDTH),
                ClampCardY(y, height),
                COMPACT_CARD_WIDTH,
                height);

            occupied.Add(rect);
            DrawCard(rect.x, rect.y, entry.Card.Value, height, entry.Card.Key);
        }

    }

    private static bool IsRightDeskGroup(int deskCode)
    {
        // DeskCode 为零基：0-3 对应 1-4 号右侧桌，4-7 对应 5-8 号左侧桌。
        return deskCode >= 0 && deskCode < 4;
    }

    private static Dictionary<int, Vector2> GetDeskScreenPoints()
    {
        // 桌子位置在营业场景中基本固定；每半秒左右重扫一次即可，避免 OnGUI 多遍调用
        // 时反复执行 FindObjectsOfType。
        if (Time.frameCount - _deskPointCacheFrame < 30 && _deskPointCache.Count > 0)
            return _deskPointCache;

        var result = new Dictionary<int, Vector2>();
        try
        {
            if (Camera.main == null) return result;
            var displayers = UnityEngine.Object.FindObjectsOfType<
                NightScene.GuestManagementUtility.GuestTableDisplayer>();
            if (displayers == null) return result;

            foreach (var displayer in displayers)
            {
                if (displayer == null || displayer.transform == null) continue;
                int deskNumber = ReadDisplayedDeskNumber(displayer);
                if (deskNumber < 1 || deskNumber > 8) continue;

                Vector3 screen = Camera.main.WorldToScreenPoint(displayer.transform.position);
                if (screen.z <= 0f) continue;
                result[deskNumber - 1] = new Vector2(screen.x, Screen.height - screen.y);
            }
        }
        catch (System.Exception e)
        {
            Plugin.Instance?.Log?.LogWarning("[MystiaRec] 读取桌号牌位置失败: " + e.Message);
        }
        _deskPointCacheFrame = Time.frameCount;
        _deskPointCache = result;
        return result;
    }

    private static int ReadDisplayedDeskNumber(object displayer)
    {
        try
        {
            var deskLabel = displayer.GetType().GetProperty("deskCode")?.GetValue(displayer);
            if (deskLabel == null) return -1;
            string text = deskLabel.GetType().GetProperty("text")?.GetValue(deskLabel)?.ToString() ?? "";
            string digits = new string(text.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int deskNumber) ? deskNumber : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static bool TryGetGuestScreenPoint(CustomerRecommendation card, out Vector2 point)
    {
        point = default;
        try
        {
            if (!card.HasCustomerWorldPosition || Camera.main == null) return false;
            Vector3 screen = Camera.main.WorldToScreenPoint(card.CustomerWorldPosition);
            if (screen.z <= 0f) return false;
            point = new Vector2(screen.x, Screen.height - screen.y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Rect ResolveLocalCollision(Rect rect, List<Rect> occupied)
    {
        for (int attempt = 0; attempt < occupied.Count + 2; attempt++)
        {
            var overlap = occupied.FirstOrDefault(other => other.Overlaps(rect));
            if (overlap.width <= 0f) break;

            float below = overlap.yMax + 6f;
            if (below + rect.height <= Screen.height - SCREEN_MARGIN)
                rect.y = below;
            else
                rect.y = Mathf.Max(SCREEN_MARGIN, overlap.yMin - rect.height - 6f);
        }
        return rect;
    }

    private static float ClampCardX(float x, float cardWidth)
    {
        float maxX = Mathf.Max(SCREEN_MARGIN, Screen.width - cardWidth - SCREEN_MARGIN);
        return Mathf.Clamp(x, SCREEN_MARGIN, maxX);
    }

    private static float ClampCardY(float y, float cardHeight)
    {
        float maxY = Mathf.Max(SCREEN_MARGIN, Screen.height - cardHeight - SCREEN_MARGIN);
        return Mathf.Clamp(y, SCREEN_MARGIN, maxY);
    }

    /// <summary>
    /// 从 Unity Input 类直接读取鼠标状态，绕过 IMGUI Event.current 可能被游戏消费的问题。
    /// 每帧只读取一次（OnGUI 可能被调用多次）。
    /// Input.mousePosition 原点在左下角，转换为 IMGUI 的左上角坐标系。
    /// </summary>
    private void CaptureInput()
    {
        if (_lastInputFrame == Time.frameCount) return;
        _lastInputFrame = Time.frameCount;

        Vector3 mp = Input.mousePosition;
        _inputMouseX = mp.x;
        _inputMouseY = Screen.height - mp.y; // 左下→左上
        _inputMouseDown = Input.GetMouseButtonDown(0);
        _inputMouseUp = Input.GetMouseButtonUp(0);
        _inputMouseHeld = Input.GetMouseButton(0);
    }

    private void ProcessDragEvents()
    {
        if (_draggedCardId < 0) return;
        if (!Plugin.ActiveRecommendations.TryGetValue(_draggedCardId, out var cr)) return;

        if (_inputMouseHeld && cr.DragX.HasValue)
        {
            cr.DragX = _inputMouseX - _dragStartMouseX + _dragStartCardX;
            cr.DragY = _inputMouseY - _dragStartMouseY + _dragStartCardY;
        }
        else if (_inputMouseUp)
        {
            _draggedCardId = -1;
        }
    }

    private float CalcCardHeight(CustomerRecommendation cr)
    {
        float contentW = GetCardWidth(cr) - CARD_PADDING * 2;
        float h = CARD_PADDING;

        if (IsPending(cr))
            return h + LINE_HEIGHT * 2 + 8 + CARD_PADDING;

        if (IsCompact(cr))
            return h + LINE_HEIGHT * 3 + 8 + CARD_PADDING;

        h += LINE_HEIGHT + 4;
        if (ShouldDrawDecisionStatus(cr))
            h += TextHeight(_taskWarningStyle, cr.StatusMessage, contentW) + 2;
        var customer = Plugin.DataEngine.GetCustomer(cr.CustomerName);
        if (customer != null)
        {
            h += TextHeight(_likeLineStyle, BuildLikeLine(customer), contentW);
            if (customer.negativeTags.Count > 0)
                h += TextHeight(_hateLineStyle, BuildHateLine(customer), contentW);
            h += 4;
        }

        int recCount = System.Math.Min(cr.Recommendations.Count, MAX_RECIPES);
        if (recCount == 0)
            h += LINE_HEIGHT + 2;
        for (int i = 0; i < recCount; i++)
        {
            var rec = cr.Recommendations[i];
            h += TextHeight(_planTitleStyle, BuildPlanTitle(cr, rec, i), contentW);
            h += TextHeight(_ingredientStyle, BuildIngredientLine(rec), contentW);
            h += TextHeight(_detailStyle, BuildPlanTagLine(cr, rec), contentW);
            h += 7;
        }

        return h + CARD_PADDING;
    }

    private void DrawCard(float x, float y, CustomerRecommendation cr, float totalH, int cardId)
    {
        GUI.color = new Color(1, 1, 1, cr.FadeAlpha);
        float cardWidth = GetCardWidth(cr);

        // 卡片背景
        GUI.DrawTexture(new Rect(x, y, cardWidth, totalH), _bgCard);

        float cy = y + CARD_PADDING;
        float contentW = cardWidth - CARD_PADDING * 2;

        // ===== 标题行（可拖拽） =====
        Rect headerRect = new Rect(x, y, cardWidth, LINE_HEIGHT + CARD_PADDING + 4);

        // 拖拽检测（使用原始 Input 输入，绕过 IMGUI Event.current 被游戏消费的问题）
        if (!_eventConsumed && _inputMouseDown
            && headerRect.Contains(new Vector2(_inputMouseX, _inputMouseY)))
        {
            _draggedCardId = cardId;
            if (!cr.DragX.HasValue)
            {
                cr.DragX = x;
                cr.DragY = y;
            }
            _dragStartMouseX = _inputMouseX;
            _dragStartMouseY = _inputMouseY;
            _dragStartCardX = cr.DragX.Value;
            _dragStartCardY = cr.DragY.Value;
            _dragOrder.Remove(cardId);
            _dragOrder.Add(cardId);
            _eventConsumed = true;
            _inputMouseDown = false;
        }

        // 拖拽手柄标识（⠿ 三点点，浅灰色）
        GUI.Label(new Rect(x + CARD_PADDING, cy, 16, LINE_HEIGHT), "⠿", _dragHandleStyle);
        string deskLabel = $"#{cr.DeskCode + 1}";
        GUI.Label(new Rect(x + CARD_PADDING + 16, cy, 28, LINE_HEIGHT), deskLabel, _deskStyle);

        if (IsPending(cr))
        {
            GUI.Label(new Rect(x + CARD_PADDING + 44, cy, contentW - 44, LINE_HEIGHT), cr.CustomerName, _titleStyle);
            cy += LINE_HEIGHT + 4;
            string status = string.IsNullOrWhiteSpace(cr.StatusMessage)
                ? (cr.PendingState == PendingRecommendationState.WaitingNextRound ? "等待下一轮" : "请对话获取需求")
                : cr.StatusMessage;
            GUI.Label(new Rect(x + CARD_PADDING + 4, cy, contentW - 4, LINE_HEIGHT),
                status, GetPendingStyle(cr));
            return;
        }

        if (IsCompact(cr))
        {
            var rec = cr.Recommendations[cr.MatchedRecommendationIndex];
            GUI.Label(new Rect(x + CARD_PADDING + 44, cy, contentW - 44, LINE_HEIGHT), cr.CustomerName, _titleStyle);
            cy += LINE_HEIGHT + 4;
            string star = rec.ExtraIngredients != null && rec.ExtraIngredients.Count > 0 ? "★" : "";
            GUI.Label(new Rect(x + CARD_PADDING + 4, cy, contentW - 4, LINE_HEIGHT), rec.RecipeName + star, _compactStyle);
            cy += LINE_HEIGHT;
            GUI.Label(new Rect(x + CARD_PADDING + 4, cy, contentW - 4, LINE_HEIGHT), rec.BeverageName, _beverageNameStyle);
            return;
        }

        string requirements = string.Join("　", new[] { cr.ReqFoodTag, cr.ReqBevTag }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        string title = string.IsNullOrEmpty(requirements)
            ? cr.CustomerName
            : $"{cr.CustomerName}　需求：{requirements}";
        GUI.Label(new Rect(x + CARD_PADDING + 44, cy, contentW - 44, LINE_HEIGHT), title, _titleStyle);
        cy += LINE_HEIGHT + 4;

        if (ShouldDrawDecisionStatus(cr))
        {
            float statusH = TextHeight(_taskWarningStyle, cr.StatusMessage, contentW);
            GUI.Label(new Rect(x + CARD_PADDING, cy, contentW, statusH), cr.StatusMessage, _taskWarningStyle);
            cy += statusH + 2;
        }

        var customer = Plugin.DataEngine.GetCustomer(cr.CustomerName);
        if (customer != null)
        {
            string likeLine = BuildLikeLine(customer);
            float likeH = TextHeight(_likeLineStyle, likeLine, contentW);
            GUI.Label(new Rect(x + CARD_PADDING, cy, contentW, likeH), likeLine, _likeLineStyle);
            cy += likeH;

            if (customer.negativeTags.Count > 0)
            {
                string hateLine = BuildHateLine(customer);
                float hateH = TextHeight(_hateLineStyle, hateLine, contentW);
                GUI.Label(new Rect(x + CARD_PADDING, cy, contentW, hateH), hateLine, _hateLineStyle);
                cy += hateH;
            }
            cy += 4;
        }

        var recs = cr.Recommendations.Take(MAX_RECIPES).ToList();
        if (recs.Count == 0)
        {
            string status = string.IsNullOrEmpty(cr.StatusMessage) ? "无可用方案" : cr.StatusMessage;
            GUI.Label(new Rect(x + CARD_PADDING, cy, contentW, LINE_HEIGHT), status, _detailStyle);
            return;
        }

        for (int i = 0; i < recs.Count; i++)
        {
            var rec = recs[i];
            string planTitle = BuildPlanTitle(cr, rec, i);
            float titleH = TextHeight(_planTitleStyle, planTitle, contentW);
            GUI.Label(new Rect(x + CARD_PADDING, cy, contentW, titleH), planTitle, _planTitleStyle);
            cy += titleH;

            string ingredientLine = BuildIngredientLine(rec);
            float ingredientH = TextHeight(_ingredientStyle, ingredientLine, contentW);
            GUI.Label(new Rect(x + CARD_PADDING, cy, contentW, ingredientH), ingredientLine, _ingredientStyle);
            cy += ingredientH;

            string tagLine = BuildPlanTagLine(cr, rec);
            float tagH = TextHeight(_detailStyle, tagLine, contentW);
            GUI.Label(new Rect(x + CARD_PADDING, cy, contentW, tagH), tagLine, _detailStyle);
            cy += tagH + 7;
        }
    }

    private static bool IsCompact(CustomerRecommendation card)
        => card.TrackingState != RecommendationTrackingState.AwaitingCook
           && card.MatchedRecommendationIndex >= 0
           && card.MatchedRecommendationIndex < card.Recommendations.Count;

    private static bool IsPending(CustomerRecommendation card)
        => card.PendingState != PendingRecommendationState.None;

    private static bool IsDecisionCard(CustomerRecommendation card)
        => !IsPending(card) && !IsCompact(card);

    private static bool ShouldDrawDecisionStatus(CustomerRecommendation card)
        => IsDecisionCard(card)
           && card.Recommendations.Count > 0
           && !string.IsNullOrWhiteSpace(card.StatusMessage);

    private static bool IsRightSidePrompt(CustomerRecommendation card)
        => card.PendingState == PendingRecommendationState.NeedsInteraction
           || card.PendingState == PendingRecommendationState.ReadingOrder;

    private static float GetCardWidth(CustomerRecommendation card)
        => IsCompact(card) || IsPending(card) ? COMPACT_CARD_WIDTH : CARD_WIDTH;

    private GUIStyle GetPendingStyle(CustomerRecommendation card)
        => card.PendingState == PendingRecommendationState.WaitingNextRound
            ? _pendingWaitingStyle
            : _pendingInteractionStyle;

    private static string BuildLikeLine(CustomerData customer)
    {
        var tags = customer.positiveTags
            .Concat(customer.beverageTags ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct();
        return "喜好：" + string.Join("　", tags);
    }

    private static string BuildHateLine(CustomerData customer)
        => "厌恶：" + string.Join("　", customer.negativeTags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct());

    private static void GetPositiveScores(CustomerRecommendation card, Recommendation rec,
        out int recipeScore, out int beverageScore, out HashSet<string> recipeTags)
    {
        var customer = Plugin.DataEngine.GetCustomer(card.CustomerName);
        var positives = new HashSet<string>(customer?.positiveTags ?? new List<string>());
        positives.UnionWith(customer?.beverageTags ?? new List<string>());

        recipeTags = new HashSet<string>(rec.RecipeTags ?? new List<string>());
        foreach (var beverageTag in rec.BeverageTags ?? new List<string>())
            recipeTags.Remove(beverageTag);

        recipeScore = recipeTags.Count(positives.Contains);
        beverageScore = (rec.BeverageTags ?? new List<string>()).Distinct().Count(positives.Contains);
    }

    private static string BuildPlanTitle(CustomerRecommendation card, Recommendation rec, int index)
    {
        GetPositiveScores(card, rec, out int recipeScore, out int beverageScore, out _);
        string nightingale = rec.NeedNightingale ? "（夜雀）" : "";
        string star = rec.ExtraIngredients != null && rec.ExtraIngredients.Count > 0 ? "★" : "";
        string planName = rec.IsFixedRecipeTask
            ? (card.Recommendations.Count > 1 ? $"D-{(char)('A' + index)}" : "D")
            : ((char)('A' + index)).ToString();
        string fallback = rec.FallbackBelowFour && !string.IsNullOrWhiteSpace(rec.FallbackReason)
            ? $"（{rec.FallbackReason}）"
            : "";
        return $"方案 {planName}{nightingale}{fallback}　{rec.RecipeName}{star}（{recipeScore}）＋ {rec.BeverageName}（{beverageScore}）　{recipeScore + beverageScore}　￥{rec.TotalPrice}";
    }

    private static string BuildIngredientLine(Recommendation rec)
    {
        var baseIngredients = rec.BaseIngredients != null && rec.BaseIngredients.Count > 0
            ? rec.BaseIngredients
            : (rec.Ingredients ?? new List<string>()).Where(i => !i.StartsWith("+")).ToList();
        string text = baseIngredients.Count > 0 ? string.Join("、", baseIngredients) : "无";
        if (rec.ExtraIngredients != null && rec.ExtraIngredients.Count > 0)
            text += "（" + string.Join("、", rec.ExtraIngredients) + "）";
        if (!string.IsNullOrWhiteSpace(rec.RequiredCooker))
            text += "（" + rec.RequiredCooker + "）";
        return text;
    }

    private static string BuildPlanTagLine(CustomerRecommendation card, Recommendation rec)
    {
        GetPositiveScores(card, rec, out _, out _, out var recipeTags);
        var customer = Plugin.DataEngine.GetCustomer(card.CustomerName);
        var positives = new HashSet<string>(customer?.positiveTags ?? new List<string>());
        positives.UnionWith(customer?.beverageTags ?? new List<string>());

        IEnumerable<string> Format(IEnumerable<string> tags) => tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .Select(t => positives.Contains(t) ? t + " +1" : t);

        return string.Join("　", Format(recipeTags).Concat(Format(rec.BeverageTags ?? new List<string>())));
    }

    private static float TextHeight(GUIStyle style, string text, float width)
        => Mathf.Max(LINE_HEIGHT, style.CalcHeight(new GUIContent(text ?? ""), width));

    private void DrawDishBadges()
    {
        GUI.color = Color.white;
        foreach (var assignment in RuntimeOrderTracker.Assignments.ToList())
        {
            if (!Plugin.ActiveRecommendations.ContainsKey(assignment.CardId)) continue;
            if (!TryGetDishScreenPosition(assignment, out var position)) continue;

            var borderRect = new Rect(position.x - 21, position.y - 30, 42, 28);
            var badgeRect = new Rect(borderRect.x + 2, borderRect.y + 2,
                borderRect.width - 4, borderRect.height - 4);
            GUI.DrawTexture(borderRect, _badgeBorderBg);
            GUI.DrawTexture(badgeRect, assignment.Completed ? _badgeCompletedBg : _badgeCookingBg);
            GUI.Label(new Rect(badgeRect.x + 1, badgeRect.y + 1, badgeRect.width, badgeRect.height),
                $"#{assignment.DeskCode + 1}", _badgeShadowStyle);
            GUI.Label(badgeRect, $"#{assignment.DeskCode + 1}", _badgeStyle);
        }
    }

    private static bool TryGetDishScreenPosition(RareDishAssignment assignment, out Vector2 position)
    {
        position = default;
        try
        {
            // 料理完成后优先跟随真正进入托盘的对象。
            if (assignment.Completed && TryGetTrayScreenPosition(assignment, out position))
                return true;

            // 仅当储藏面板打开且对应料理图标当前可见时，编码才跟随储藏格显示。
            if (assignment.Completed && TryGetStorageScreenPosition(assignment, out position))
                return true;

            // 料理已经离开厨具后，即使它随后被交付/移出托盘，也不能让标记跳回旧厨具。
            if (assignment.Extracted)
                return false;

            var controller = assignment.Controller;
            if (controller == null || Camera.main == null) return false;
            Vector3 world = controller.transform.position;
            if (controller.resultVisual != null)
                world = controller.resultVisual.transform.position;
            var screen = Camera.main.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;
            position = new Vector2(screen.x, Screen.height - screen.y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetTrayScreenPosition(RareDishAssignment assignment, out Vector2 position)
    {
        position = default;
        if (!RuntimeOrderTracker.TryResolveTrayIndex(assignment, out int trayIndex)) return false;
        try
        {
            var tray = GameData.RunTime.NightSceneUtility.IzakayaTray.Instance?.Tray;
            var elements = tray?.Elements;
            if (elements == null) return false;

            if (trayIndex >= elements.Length) return false;
            var current = elements[trayIndex];
            if (!RuntimeOrderTracker.IsSameSellable(assignment.Result, current)) return false;

            return TryGetActualTraySlotScreenPosition(trayIndex, out position);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetActualTraySlotScreenPosition(int trayIndex, out Vector2 position)
    {
        position = default;
        if (trayIndex < 0) return false;
        try
        {
            var panel = GetActiveTrayPanel();
            var instances = panel?.m_AllTrayInstances;
            if (instances == null) return false;

            int slotCount = instances.Count;
            try
            {
                int gameSlotCount = GameData.RunTime.NightSceneUtility.IzakayaTray.Instance?.TrayMaxNum ?? 0;
                if (gameSlotCount > 0)
                    slotCount = Mathf.Min(slotCount, gameSlotCount);
            }
            catch { }

            var screenPoints = new List<Vector2>();
            for (int i = 0; i < instances.Count; i++)
            {
                var cluster = instances[i].Item1;
                if (cluster == null || cluster.transform == null
                    || !cluster.transform.gameObject.activeInHierarchy)
                    continue;
                if (TryGetUiScreenPosition(cluster.transform, out var screenPoint))
                    screenPoints.Add(screenPoint);
            }

            // 实例池顺序不作为槽位顺序；画面中从左到右就是托盘索引从小到大。
            screenPoints = screenPoints.OrderBy(p => p.x).Take(slotCount).ToList();
            if (trayIndex >= screenPoints.Count) return false;
            position = screenPoints[trayIndex];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static WorkSceneTrayPannel GetActiveTrayPanel()
    {
        try
        {
            if (_trayPanelCache != null && _trayPanelCache.gameObject.activeInHierarchy)
                return _trayPanelCache;
            if (Time.frameCount - _trayPanelLookupFrame < 30) return null;

            _trayPanelLookupFrame = Time.frameCount;
            _trayPanelCache = UnityEngine.Object.FindObjectsOfType<WorkSceneTrayPannel>()
                .FirstOrDefault(panel => panel != null && panel.gameObject.activeInHierarchy);
            return _trayPanelCache;
        }
        catch
        {
            _trayPanelCache = null;
            return null;
        }
    }

    private static bool TryGetUiScreenPosition(Transform transform, out Vector2 position)
    {
        position = default;
        if (transform == null) return false;

        Vector3 worldCenter = transform.position;
        if (transform is RectTransform rectTransform)
            worldCenter = rectTransform.TransformPoint(rectTransform.rect.center);

        Camera uiCamera = null;
        var canvas = transform.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = canvas.worldCamera;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, worldCenter);
        position = new Vector2(screen.x, Screen.height - screen.y);
        return true;
    }

    private static bool TryGetStorageScreenPosition(RareDishAssignment assignment, out Vector2 position)
    {
        position = default;
        try
        {
            if (!RuntimeOrderTracker.TryGetStorageTransform(assignment, out var transform)
                || transform == null)
                return false;

            return TryGetUiScreenPosition(transform, out position);
        }
        catch
        {
            return false;
        }
    }

    private void InitStyles(int fontSize, float opacity)
    {
        _bgOpacity = opacity;
        _bgCard = new Texture2D(1, 1);
        _bgCard.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.12f, opacity * 0.55f));
        _bgCard.Apply();
        _badgeBorderBg = new Texture2D(1, 1);
        _badgeBorderBg.SetPixel(0, 0, new Color(0.015f, 0.015f, 0.02f, 0.98f));
        _badgeBorderBg.Apply();
        _badgeCookingBg = new Texture2D(1, 1);
        _badgeCookingBg.SetPixel(0, 0, new Color(0.08f, 0.45f, 0.95f, 0.92f));
        _badgeCookingBg.Apply();
        _badgeCompletedBg = new Texture2D(1, 1);
        _badgeCompletedBg.SetPixel(0, 0, new Color(0.12f, 0.72f, 0.28f, 0.92f));
        _badgeCompletedBg.Apply();

        int tagFontSize = System.Math.Max(fontSize - 1, 10);

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 2,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.84f, 0f) },
            clipping = TextClipping.Overflow
        };

        _deskStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 1,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.3f, 0.8f, 1f) },
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Overflow
        };

        _tagPosStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            normal = { textColor = new Color(0.3f, 0.95f, 0.3f) },
            clipping = TextClipping.Overflow
        };

        _tagNegStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            normal = { textColor = new Color(1f, 0.35f, 0.35f) },
            clipping = TextClipping.Overflow
        };

        _tagBevStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            normal = { textColor = new Color(1f, 0.85f, 0.2f) },
            clipping = TextClipping.Overflow
        };

        _tagBevPrefStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            normal = { textColor = new Color(0.7f, 0.55f, 0.95f) },
            clipping = TextClipping.Overflow
        };

        _tagReqStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.2f, 1f, 1f) },
            clipping = TextClipping.Overflow
        };

        _detailStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
            wordWrap = true,
            clipping = TextClipping.Clip
        };

        _ratingPerfectStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 1,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.84f, 0f) },
            clipping = TextClipping.Overflow
        };
        _ratingGoodStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 1,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.3f, 0.95f, 0.3f) },
            clipping = TextClipping.Overflow
        };
        _ratingOkStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 1,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.5f, 0.8f, 1f) },
            clipping = TextClipping.Overflow
        };

        _sectionToggleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.5f, 0.5f, 0.6f) },
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Overflow
        };

        _sectionHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            normal = { textColor = new Color(0.5f, 0.5f, 0.6f) },
            clipping = TextClipping.Overflow
        };

        _scorePosStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.3f, 0.95f, 0.3f) },
            clipping = TextClipping.Overflow
        };

        _scoreZeroStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
            clipping = TextClipping.Overflow
        };

        _dragHandleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize + 2,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.5f, 0.5f, 0.6f) },
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Overflow
        };

        _recipeNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 1,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.93f, 0.73f) },  // 暖米黄 #ffeebb
            clipping = TextClipping.Overflow
        };

        _beverageNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 1,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.80f, 0.40f) },  // 琥珀金 #ffcc66
            clipping = TextClipping.Overflow
        };

        _ingredientStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.87f, 0.93f, 1f) },  // 浅蓝白 #ddeeff
            wordWrap = true,
            clipping = TextClipping.Clip
        };

        _likeLineStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.36f, 0.36f) },
            wordWrap = true,
            clipping = TextClipping.Clip
        };

        _hateLineStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = tagFontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.68f, 0.55f, 1f) },
            wordWrap = true,
            clipping = TextClipping.Clip
        };

        _planTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.86f, 0.3f) },
            wordWrap = true,
            clipping = TextClipping.Clip
        };

        _compactStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 1,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.93f, 0.73f) },
            clipping = TextClipping.Overflow
        };

        _pendingInteractionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.58f, 0.16f) },
            clipping = TextClipping.Overflow
        };

        _pendingWaitingStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.86f, 0.3f) },
            clipping = TextClipping.Overflow
        };

        _taskWarningStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.5f, 0.2f) },
            wordWrap = true,
            clipping = TextClipping.Clip
        };

        _badgeShadowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 2,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.black },
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip
        };

        _badgeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 2,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip
        };
    }
}

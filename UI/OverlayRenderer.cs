using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MystiaRecommendation.Engine;

namespace MystiaRecommendation.UI;

/// <summary>
/// 多稀客推荐叠加渲染器 - 可拖拽+折叠卡片
/// </summary>
public class OverlayRenderer
{
    private bool _stylesInitialized;
    private Texture2D _bgCard;
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

    // 布局常量
    private const float CARD_WIDTH = 310;
    private const float CARD_PADDING = 8;
    private const float CARD_SPACING = 10;
    private const float SCREEN_MARGIN = 10;
    private const int MAX_PER_COLUMN = 4;
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
            .Take(MAX_PER_COLUMN * 2)
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
            Plugin.ActiveRecommendations.Remove(key);
            _dragOrder.Remove(key);
            if (_draggedCardId == key) _draggedCardId = -1;
        }

        // 读取原始鼠标输入（绕过 IMGUI Event.current，解决游戏内其他UI消费事件的问题）
        CaptureInput();

        _eventConsumed = false;

        // 处理拖拽中的 MouseDrag / MouseUp
        ProcessDragEvents();

        // 分两组：自动列布局 vs 手动拖拽位置
        var autoCards = active.Where(kv => !kv.Value.DragX.HasValue).ToList();
        var draggedCards = active.Where(kv => kv.Value.DragX.HasValue).ToList();

        // === 阶段1：列布局绘制自动卡片 ===
        var cardHeights = new List<float>();
        foreach (var kv in autoCards)
            cardHeights.Add(CalcCardHeight(kv.Value));

        int leftCount = System.Math.Min(autoCards.Count, MAX_PER_COLUMN);
        int rightCount = System.Math.Min(System.Math.Max(0, autoCards.Count - MAX_PER_COLUMN), MAX_PER_COLUMN);

        float rightEdge = Screen.width - SCREEN_MARGIN;
        float leftColX = rightEdge - CARD_WIDTH;
        float rightColX = leftColX - CARD_WIDTH - 12;

        float cy = SCREEN_MARGIN;
        for (int i = 0; i < leftCount; i++)
        {
            DrawCard(leftColX, cy, autoCards[i].Value, cardHeights[i], autoCards[i].Key);
            cy += cardHeights[i] + CARD_SPACING;
        }

        if (rightCount > 0)
        {
            cy = SCREEN_MARGIN;
            for (int i = leftCount; i < leftCount + rightCount; i++)
            {
                DrawCard(rightColX, cy, autoCards[i].Value, cardHeights[i], autoCards[i].Key);
                cy += cardHeights[i] + CARD_SPACING;
            }
        }

        // === 阶段2：绘制拖拽卡片（在列布局之上，按Z序） ===
        foreach (var cardId in _dragOrder)
        {
            var kv = draggedCards.FirstOrDefault(k => k.Key == cardId);
            if (kv.Key == 0 && kv.Value == null) continue;
            if (!kv.Value.DragX.HasValue) continue;
            float h = CalcCardHeight(kv.Value);
            DrawCard(kv.Value.DragX.Value, kv.Value.DragY.Value, kv.Value, h, kv.Key);
        }

        // 清理不在活跃列表中的拖拽排序
        var activeIds = new HashSet<int>(active.Select(k => k.Key));
        _dragOrder.RemoveAll(id => !activeIds.Contains(id));
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
        float h = CARD_PADDING;
        h += LINE_HEIGHT + 4; // 标题行

        // 概述区 toggle 行（始终显示）
        h += TAG_LINE_HEIGHT + 2;

        if (!cr.OverviewCollapsed)
        {
            var customer = Plugin.DataEngine.GetCustomer(cr.CustomerName);
            if (customer != null)
            {
                int tagCount = customer.positiveTags.Count + customer.negativeTags.Count
                    + (customer.beverageTags?.Count ?? 0);
                if (!string.IsNullOrEmpty(cr.ReqFoodTag)) tagCount++;
                if (!string.IsNullOrEmpty(cr.ReqBevTag)) tagCount++;
                int tagRows = System.Math.Max(1, (int)System.Math.Ceiling(tagCount * 55.0 / (CARD_WIDTH - CARD_PADDING * 2)));
                h += tagRows * TAG_LINE_HEIGHT + 4;
            }
            else
            {
                int tagCount = 0;
                if (!string.IsNullOrEmpty(cr.ReqFoodTag)) tagCount++;
                if (!string.IsNullOrEmpty(cr.ReqBevTag)) tagCount++;
                h += System.Math.Max(1, tagCount) * TAG_LINE_HEIGHT + 4;
            }

            if (!string.IsNullOrEmpty(cr.StatusMessage))
                h += LINE_HEIGHT;
        }

        // 方案区
        int recCount = System.Math.Min(cr.Recommendations.Count, MAX_RECIPES);
        if (recCount == 0)
        {
            h += LINE_HEIGHT + 2;
        }
        for (int i = 0; i < recCount; i++)
        {
            h += LINE_HEIGHT + 2; // toggle 行
            bool collapsed = (i == 0) ? cr.Rec1Collapsed : cr.Rec2Collapsed;
            if (!collapsed)
            {
                var rec = cr.Recommendations[i];
                if (rec.RecipeTags != null && rec.RecipeTags.Count > 0)
                    h += LINE_HEIGHT;
                if (rec.BeverageTags != null && rec.BeverageTags.Count > 0)
                    h += LINE_HEIGHT;
                h += LINE_HEIGHT + 2; // 食材+厨具+总价
            }
        }

        h += CARD_PADDING;
        return h;
    }

    private void DrawCard(float x, float y, CustomerRecommendation cr, float totalH, int cardId)
    {
        GUI.color = new Color(1, 1, 1, cr.FadeAlpha);

        // 卡片背景
        GUI.DrawTexture(new Rect(x, y, CARD_WIDTH, totalH), _bgCard);

        float cy = y + CARD_PADDING;
        float contentW = CARD_WIDTH - CARD_PADDING * 2;

        // ===== 标题行（可拖拽） =====
        Rect headerRect = new Rect(x, y, CARD_WIDTH, LINE_HEIGHT + CARD_PADDING + 4);

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
        GUI.Label(new Rect(x + CARD_PADDING + 44, cy, contentW - 44, LINE_HEIGHT),
            $"★ {cr.CustomerName}", _titleStyle);
        cy += LINE_HEIGHT + 4;

        // 标题栏底部分隔线，标识可拖拽区域边界
        GUI.DrawTexture(new Rect(x + CARD_PADDING, cy - 2, contentW, 1), Texture2D.whiteTexture,
            ScaleMode.StretchToFill, true, 0, new Color(0.3f, 0.3f, 0.5f, 0.5f), 0, 0);

        // ===== 概述区 =====
        string ovToggle = cr.OverviewCollapsed ? "▶" : "▼";
        Rect ovToggleRect = new Rect(x + CARD_PADDING, cy, 18, TAG_LINE_HEIGHT);
        GUI.Label(ovToggleRect, ovToggle, _sectionToggleStyle);
        GUI.Label(new Rect(x + CARD_PADDING + 18, cy, contentW - 18, TAG_LINE_HEIGHT),
            "稀客需求", _sectionHeaderStyle);

        // 折叠切换（使用原始 Input 输入）
        if (!_eventConsumed && _inputMouseDown
            && ovToggleRect.Contains(new Vector2(_inputMouseX, _inputMouseY)))
        {
            cr.OverviewCollapsed = !cr.OverviewCollapsed;
            _eventConsumed = true;
            _inputMouseDown = false;
        }
        cy += TAG_LINE_HEIGHT + 2;

        if (!cr.OverviewCollapsed)
        {
            var customer = Plugin.DataEngine.GetCustomer(cr.CustomerName);
            if (customer != null)
            {
                float tagX = x + CARD_PADDING;
                float tagMaxX = x + CARD_WIDTH - CARD_PADDING;
                float tagRowH = TAG_LINE_HEIGHT;

                // 正面标签
                foreach (var tag in customer.positiveTags)
                {
                    string text = "+" + tag;
                    float tw = _tagPosStyle.CalcSize(new GUIContent(text)).x + 6;
                    if (tagX + tw > tagMaxX && tagX > x + CARD_PADDING + 1)
                    {
                        tagX = x + CARD_PADDING;
                        cy += tagRowH;
                    }
                    GUI.Label(new Rect(tagX, cy, tw, tagRowH), text, _tagPosStyle);
                    tagX += tw + 2;
                }
                // 负面标签
                foreach (var tag in customer.negativeTags)
                {
                    string text = "-" + tag;
                    float tw = _tagNegStyle.CalcSize(new GUIContent(text)).x + 6;
                    if (tagX + tw > tagMaxX && tagX > x + CARD_PADDING + 1)
                    {
                        tagX = x + CARD_PADDING;
                        cy += tagRowH;
                    }
                    GUI.Label(new Rect(tagX, cy, tw, tagRowH), text, _tagNegStyle);
                    tagX += tw + 2;
                }
                // 酒水喜好标签（紫色，跟在负面标签后面）
                if (customer.beverageTags != null)
                {
                    foreach (var tag in customer.beverageTags)
                    {
                        float tw = _tagBevPrefStyle.CalcSize(new GUIContent(tag)).x + 6;
                        if (tagX + tw > tagMaxX && tagX > x + CARD_PADDING + 1)
                        {
                            tagX = x + CARD_PADDING;
                            cy += tagRowH;
                        }
                        GUI.Label(new Rect(tagX, cy, tw, tagRowH), tag, _tagBevPrefStyle);
                        tagX += tw + 2;
                    }
                }
                // 食/饮标签
                if (!string.IsNullOrEmpty(cr.ReqFoodTag))
                {
                    string text = "食:" + cr.ReqFoodTag;
                    float tw = _tagReqStyle.CalcSize(new GUIContent(text)).x + 6;
                    if (tagX + tw > tagMaxX && tagX > x + CARD_PADDING + 1)
                    {
                        tagX = x + CARD_PADDING;
                        cy += tagRowH;
                    }
                    GUI.Label(new Rect(tagX, cy, tw, tagRowH), text, _tagReqStyle);
                    tagX += tw + 2;
                }
                if (!string.IsNullOrEmpty(cr.ReqBevTag))
                {
                    string text = "饮:" + cr.ReqBevTag;
                    float tw = _tagBevStyle.CalcSize(new GUIContent(text)).x + 6;
                    if (tagX + tw > tagMaxX && tagX > x + CARD_PADDING + 1)
                    {
                        tagX = x + CARD_PADDING;
                        cy += tagRowH;
                    }
                    GUI.Label(new Rect(tagX, cy, tw, tagRowH), text, _tagBevStyle);
                }
                cy += tagRowH + 4;
            }
            else
            {
                float tagX = x + CARD_PADDING;
                if (!string.IsNullOrEmpty(cr.ReqFoodTag))
                {
                    string text = "食:" + cr.ReqFoodTag;
                    float tw = _tagReqStyle.CalcSize(new GUIContent(text)).x + 6;
                    GUI.Label(new Rect(tagX, cy, tw, TAG_LINE_HEIGHT), text, _tagReqStyle);
                    tagX += tw + 2;
                }
                if (!string.IsNullOrEmpty(cr.ReqBevTag))
                {
                    string text = "饮:" + cr.ReqBevTag;
                    float tw = _tagBevStyle.CalcSize(new GUIContent(text)).x + 6;
                    GUI.Label(new Rect(tagX, cy, tw, TAG_LINE_HEIGHT), text, _tagBevStyle);
                }
                cy += TAG_LINE_HEIGHT + 4;
            }

            if (!string.IsNullOrEmpty(cr.StatusMessage))
            {
                GUI.Label(new Rect(x + CARD_PADDING + 4, cy, contentW - 4, LINE_HEIGHT), cr.StatusMessage, _detailStyle);
                cy += LINE_HEIGHT;
            }
        }

        // ===== 推荐方案区 =====
        var recs = cr.Recommendations.Take(MAX_RECIPES).ToList();
        if (recs.Count == 0)
        {
            GUI.Label(new Rect(x + CARD_PADDING + 4, cy, contentW - 4, LINE_HEIGHT), "无可用方案", _detailStyle);
            return;
        }

        var customerForScore = Plugin.DataEngine.GetCustomer(cr.CustomerName);
        var posTags = customerForScore?.positiveTags ?? new List<string>();
        var negTags = customerForScore?.negativeTags ?? new List<string>();
        var bevPrefs = customerForScore?.beverageTags ?? new List<string>();
        var allPositive = new HashSet<string>(posTags.Concat(bevPrefs));

        for (int i = 0; i < recs.Count; i++)
        {
            var rec = recs[i];
            bool collapsed = (i == 0) ? cr.Rec1Collapsed : cr.Rec2Collapsed;
            string recToggle = collapsed ? "▶" : "▼";
            Rect recToggleRect = new Rect(x + CARD_PADDING + 4, cy, 18, LINE_HEIGHT);

            GUI.Label(recToggleRect, recToggle, _sectionToggleStyle);

            // 折叠切换（使用原始 Input 输入）
            if (!_eventConsumed && _inputMouseDown
                && recToggleRect.Contains(new Vector2(_inputMouseX, _inputMouseY)))
            {
                if (i == 0) cr.Rec1Collapsed = !cr.Rec1Collapsed;
                else cr.Rec2Collapsed = !cr.Rec2Collapsed;
                _eventConsumed = true;
                _inputMouseDown = false;
            }

            // 料理评分 = 总标签中去掉酒水标签后的正负匹配
            var recipeOnlyTags = new HashSet<string>(rec.RecipeTags ?? new List<string>());
            if (rec.BeverageTags != null)
                foreach (var bt in rec.BeverageTags) recipeOnlyTags.Remove(bt);
            int recipeScore = recipeOnlyTags.Count(allPositive.Contains) - recipeOnlyTags.Count(negTags.Contains);
            int bevScore = (rec.BeverageTags ?? new List<string>()).Count(allPositive.Contains)
                         - (rec.BeverageTags ?? new List<string>()).Count(negTags.Contains);

            string recipeScoreStr = recipeScore > 0 ? $"+{recipeScore}" : recipeScore.ToString();
            string bevScoreStr = bevScore > 0 ? $"+{bevScore}" : bevScore.ToString();
            var recipeScoreStyle = recipeScore > 0 ? _scorePosStyle : _scoreZeroStyle;
            var bevScoreStyleFinal = bevScore > 0 ? _scorePosStyle : _scoreZeroStyle;

            float rx = x + CARD_PADDING + 4 + 18;

            // 评级颜色
            var hs = rec.ExpectedRating switch
            {
                "完美" => _ratingPerfectStyle,
                "优秀" => _ratingGoodStyle,
                _ => _ratingOkStyle
            };

            // 标题行：[评级] 料理名 +N + 酒水名 +N 💰价格
            string ratingText = $"[{rec.ExpectedRating}] ";
            GUI.Label(new Rect(rx, cy, hs.CalcSize(new GUIContent(ratingText)).x, LINE_HEIGHT), ratingText, hs);
            rx += hs.CalcSize(new GUIContent(ratingText)).x;

            string recipeText = $"{rec.RecipeName} ";
            GUI.Label(new Rect(rx, cy, _recipeNameStyle.CalcSize(new GUIContent(recipeText)).x, LINE_HEIGHT), recipeText, _recipeNameStyle);
            rx += _recipeNameStyle.CalcSize(new GUIContent(recipeText)).x;

            string rsBadge = $" {recipeScoreStr} ";
            GUI.Label(new Rect(rx, cy, recipeScoreStyle.CalcSize(new GUIContent(rsBadge)).x, LINE_HEIGHT), rsBadge, recipeScoreStyle);
            rx += recipeScoreStyle.CalcSize(new GUIContent(rsBadge)).x;

            string plusText = " + ";
            GUI.Label(new Rect(rx, cy, _detailStyle.CalcSize(new GUIContent(plusText)).x, LINE_HEIGHT), plusText, _detailStyle);
            rx += _detailStyle.CalcSize(new GUIContent(plusText)).x;

            string bevText = $"{rec.BeverageName} ";
            GUI.Label(new Rect(rx, cy, _beverageNameStyle.CalcSize(new GUIContent(bevText)).x, LINE_HEIGHT), bevText, _beverageNameStyle);
            rx += _beverageNameStyle.CalcSize(new GUIContent(bevText)).x;

            string bsBadge = $" {bevScoreStr} ";
            GUI.Label(new Rect(rx, cy, bevScoreStyleFinal.CalcSize(new GUIContent(bsBadge)).x, LINE_HEIGHT), bsBadge, bevScoreStyleFinal);
            rx += bevScoreStyleFinal.CalcSize(new GUIContent(bsBadge)).x;

            string priceText = $" 💰{rec.TotalPrice}";
            GUI.Label(new Rect(rx, cy, contentW - (rx - x - CARD_PADDING - 4), LINE_HEIGHT), priceText, _detailStyle);

            cy += LINE_HEIGHT + 2;

            if (!collapsed)
            {
                // 料理标签
                if (rec.RecipeTags != null && rec.RecipeTags.Count > 0)
                {
                    string tags = string.Join(" ", rec.RecipeTags.Take(7));
                    GUI.Label(new Rect(x + CARD_PADDING + 4, cy, contentW - 4, LINE_HEIGHT), $"🏷️{tags}", _detailStyle);
                    cy += LINE_HEIGHT;
                }

                // 酒水标签
                if (rec.BeverageTags != null && rec.BeverageTags.Count > 0)
                {
                    string tags = string.Join(" ", rec.BeverageTags.Take(5));
                    GUI.Label(new Rect(x + CARD_PADDING + 4, cy, contentW - 4, LINE_HEIGHT), $"饮:{tags}", _tagBevStyle);
                    cy += LINE_HEIGHT;
                }

                // 食材+厨具+总价
                string ingredients = rec.Ingredients.Count > 0 ? string.Join(",", rec.Ingredients) : "无";
                string budgetFlag = rec.OverBudget ? "⚠" : "";
                GUI.Label(new Rect(x + CARD_PADDING + 4, cy, contentW - 4, LINE_HEIGHT),
                    $"🥢{ingredients}  🔧{rec.RequiredCooker}{budgetFlag}", _ingredientStyle);
                cy += LINE_HEIGHT + 2;
            }
        }
    }

    private void InitStyles(int fontSize, float opacity)
    {
        _bgOpacity = opacity;
        _bgCard = new Texture2D(1, 1);
        _bgCard.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.12f, opacity * 0.55f));
        _bgCard.Apply();

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
            clipping = TextClipping.Overflow
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
            clipping = TextClipping.Overflow
        };
    }
}

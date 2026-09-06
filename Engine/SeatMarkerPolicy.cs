namespace MystiaRecommendation.Engine;

internal enum SeatMarkerItemKind
{
    Unknown,
    Food,
    Beverage
}

/// <summary>
/// 座位号用于区分外观相近的料理；酒水及未知物品不参与绑定。
/// </summary>
internal static class SeatMarkerPolicy
{
    internal static bool CanCarrySeatMarker(SeatMarkerItemKind itemKind)
        => itemKind == SeatMarkerItemKind.Food;

    internal static bool CanBindByNumericId(
        SeatMarkerItemKind itemKind,
        int expectedFoodId,
        int actualItemId)
        => CanCarrySeatMarker(itemKind)
           && expectedFoodId >= 0
           && actualItemId == expectedFoodId;
}

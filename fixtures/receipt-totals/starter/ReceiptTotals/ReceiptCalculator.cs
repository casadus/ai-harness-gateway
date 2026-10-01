namespace ReceiptTotals;

public sealed record LineItem(decimal UnitPrice, int Quantity);

public static class ReceiptCalculator
{
    public static decimal Subtotal(IEnumerable<LineItem> items)
    {
        return items.Sum(item => item.UnitPrice + item.Quantity);
    }
}

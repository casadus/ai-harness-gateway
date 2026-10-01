using ReceiptTotals;

var cases = new (string Name, LineItem[] Items, decimal Expected)[]
{
    ("multiple items", [new(12.50m, 2), new(4.25m, 3)], 37.75m),
    ("zero quantity", [new(8.00m, 0)], 0m),
    ("empty receipt", [], 0m)
};

var failures = 0;
foreach (var testCase in cases)
{
    var actual = ReceiptCalculator.Subtotal(testCase.Items);
    if (actual == testCase.Expected)
    {
        Console.WriteLine($"PASS {testCase.Name}");
    }
    else
    {
        failures++;
        Console.Error.WriteLine($"FAIL {testCase.Name}: expected {testCase.Expected}, got {actual}");
    }
}

return failures == 0 ? 0 : 1;

# Receipt totals task (revision `receipt-totals-v1`)

Inspect the C# project in this directory. The receipt subtotal is wrong for quantities other than one. Fix the production code so each item's unit price is multiplied by its quantity and the results are summed. Keep the public `Subtotal` method signature.

Run `dotnet run --project .\Checks\Checks.csproj` and report the check result. Make the change only in this disposable task copy.

Expected result: the check prints three `PASS` lines and exits with code 0. The unmodified starter prints failures and exits with code 1.

The evaluation runner copies this task into a new ignored `.local/evaluations/<run-id>` directory. Use a fresh copy for every comparison. Its `result.json` records route, versions, duration, and check outcome. Review the harness session to fill tool behavior, human intervention, and overall success. Leave unavailable fields as `null`.

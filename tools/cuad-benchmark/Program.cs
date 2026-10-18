// Runs LegalDesk's deterministic rule engine on every contract exported from CUAD and writes, per contract, the rule
// keys it raised. No model and no corpus service are involved: this measures the rules alone.
//
//   dotnet run -- contracts.jsonl predictions.jsonl

using System.Diagnostics;
using System.Text.Json;
using SignFlow.Application.Common.Services;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: dotnet run -- <contracts.jsonl> <predictions.jsonl>");
    return 1;
}

var watch = Stopwatch.StartNew();
var count = 0;
using var output = new StreamWriter(args[1]);
foreach (var line in File.ReadLines(args[0]))
{
    using var doc = JsonDocument.Parse(line);
    var title = doc.RootElement.GetProperty("title").GetString()!;
    var text = doc.RootElement.GetProperty("text").GetString()!;
    var keys = ContractRuleEngine.Analyze(text).Findings
        .Where(f => f.RuleKey is not null)
        .Select(f => f.RuleKey!)
        .Distinct()
        .OrderBy(k => k)
        .ToList();
    output.WriteLine(JsonSerializer.Serialize(new { title, rule_keys = keys }));
    count++;
}

Console.WriteLine($"{count} contracts analysed in {watch.Elapsed.TotalSeconds:0.0} s");
return 0;

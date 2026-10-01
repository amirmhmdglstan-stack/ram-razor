using RAMRazor.Core;

// Local logic test console (runs on any OS — Linux/macOS/Windows).
// The full Windows self-test lives in RAMRazor.exe --selftest (CI);
// this project verifies the shared platform-neutral logic locally.

var results = LogicSelfCheck.RunPure();
foreach (var (name, pass, note) in results)
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}  {note}");

int ok = results.Count(r => r.Pass);
Console.WriteLine($"SUMMARY: {ok}/{results.Count} passed");
return results.All(r => r.Pass) ? 0 : 1;

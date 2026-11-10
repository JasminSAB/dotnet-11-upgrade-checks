// .NET 10 to .NET 11: documented breaking changes, observed on the runtime this process runs on.
//   dotnet run -c Release -f net10.0   -> every check must show the .NET 10 behavior
//   dotnet run -c Release -f net11.0   -> every check must show the .NET 11 behavior
// verify.ps1 runs both and prints them side by side.
using System.Runtime.Intrinsics.X86;
using DotnetUpgradeChecks;

// "worker" mode: a plain host whose only BackgroundService fails. verify.ps1 reads the process
// exit code, which is what Docker, Kubernetes or systemd see.
if (args is ["worker"])
{
    await Checks.RunHostWithFailingWorker();
    return 0;
}

int major = Environment.Version.Major;
Console.WriteLine($".NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
Console.WriteLine();

// The .NET 11 hardware baseline on x86/x64 is x86-64-v2. Check it before anything else.
if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture is System.Runtime.InteropServices.Architecture.X64 or System.Runtime.InteropServices.Architecture.X86)
{
    bool v2 = Sse3.IsSupported && Ssse3.IsSupported && Sse41.IsSupported && Sse42.IsSupported && Popcnt.IsSupported;
    Console.WriteLine($"[{(v2 ? "PASS" : "FAIL")}] This CPU meets the .NET 11 x86-64-v2 baseline");
    Console.WriteLine($"       SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT: {(v2 ? "all present" : "missing")}");
    Console.WriteLine();
}

var failed = 0;
foreach (var check in Checks.All)
{
    string expected = major >= 11 ? check.Net11 : check.Net10;
    string observed = await check.Observe();
    bool ok = observed == expected;
    if (!ok) failed++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {check.Name}");
    Console.WriteLine($"       observed: {observed}");
    Console.WriteLine($"       docs: .NET 10 {check.Net10} | .NET 11 {check.Net11}");
    Console.WriteLine($"       {check.Docs}");
}

Console.WriteLine();
Console.WriteLine("SQL that EF Core generated for Math.Max:");
Console.WriteLine(Checks.LastSql);
Console.WriteLine();
Console.WriteLine(failed == 0 ? $"All checks show the documented .NET {major} behavior." : $"{failed} check(s) differ from the documentation.");
return failed == 0 ? 0 : 1;

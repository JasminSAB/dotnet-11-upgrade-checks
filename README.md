# .NET 10 to .NET 11: Behavior Changes, Checked on Both Runtimes

Source code for the dotnetleet.com post
[Upgrade to .NET 11: Nine Behaviors That Change Without a Compile Error](https://dotnetleet.com/Blogs/UpgradeToDotNet11).

Your .NET 10 code compiles on .NET 11 without a single error, and some of it does something else.
This console application targets `net10.0` and `net11.0` and runs the same checks on both runtimes.
Each check is taken from the official Microsoft breaking-changes documentation and must show the
documented .NET 10 behavior on .NET 10 and the documented .NET 11 behavior on .NET 11.

## Requirements

- .NET 11 SDK 11.0.100 or later ([download](https://dotnet.microsoft.com/download/dotnet/11.0))
- .NET 10 runtime ([download](https://dotnet.microsoft.com/download/dotnet/10.0))
- No database: the EF Core check only generates SQL

## Run

```
dotnet run -c Release -f net10.0
dotnet run -c Release -f net11.0
```

Both runs and a side-by-side comparison (Windows PowerShell or PowerShell 7):

```
pwsh ./verify.ps1
```

## What the checks show

`verify.ps1` on SDK 11.0.100, .NET 10.0.12 and .NET 11.0.0, Windows x64:

| Check | .NET 10 | .NET 11 |
|---|---|---|
| `Math.Round(655.925, 2, MidpointRounding.AwayFromZero)` | 655.93 | 655.92 |
| `Math.Round(1.5, 16)` | ArgumentOutOfRangeException | 1.5 |
| `unchecked((short)65578.0)` | 42 | 32767 |
| A `BackgroundService` throws, `await host.RunAsync()` | completes | throws InvalidOperationException |
| Same host, process exit code | 0 | -532462766 (0xE0434352, unhandled exception) |
| Two scoped services throw in `Dispose()` | InvalidOperationException, only Second disposed | AggregateException (2), Second and First disposed |
| Chained configuration sets `Endpoint` to `""` | `'https://example.com'` | `''` |
| Read a ZIP entry with one changed byte | reads `Total: 900.00 EUR` | InvalidDataException |
| `DateOnly.TryParseExact` with an invalid `DateTimeStyles` | False | ArgumentException |
| EF Core SQL for `Math.Max` on SQL Server | selects both columns, Math.Max runs in .NET | `SELECT GREATEST(...)`, needs SQL Server 2022 |

The program also checks that the CPU meets the .NET 11 x86-64-v2 baseline.

## Files

| File | What it shows |
|---|---|
| `Checks.cs` | One entry per breaking change: the documented .NET 10 and .NET 11 behavior, the docs link, and the code that observes it |
| `Program.cs` | Runs the checks on the current runtime; `worker` mode runs a host whose BackgroundService fails |
| `verify.ps1` | Runs both target frameworks, compares them and checks the worker's exit code |

## Official Microsoft documentation

- [Breaking changes in .NET 11](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11)
- [Breaking changes in EF Core 11](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes)
- [Breaking changes in ASP.NET Core 11](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/overview)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

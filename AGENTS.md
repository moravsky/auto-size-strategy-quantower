# AutoSizeStrategy Project Guide

## Build & Deploy Commands
- **Build only**: `dotnet build AutoSizeStrategy/AutoSizeStrategy.csproj -c Release`
- **Test**: `dotnet test AutoSizeStrategy.Test/AutoSizeStrategy.Test.csproj`
- **Deploy (Dev)**: `powershell ./deploy.ps1 -Target Dev`
- **Deploy (Prod)**: `powershell ./deploy.ps1 -Target Prod`
- **E2E Test Plan**: Refer to `E2E_TEST_PLAN.md`.

## Environment & Tech Stack
- **Framework**: .NET 8.0 (Targeted for Quantower compatibility) 
- **Architecture**: x64 (Required for Quantower/QuantowerDev) 
- **Platform**: Windows 11 ARM (Running x64 via Prism/Emulation)
- **Domain**: TradingPlatform.BusinessLayer (Quantower API) 

## Coding Standards
- **Style**: Use C# 14 features where compatible with .NET 8.0 (Primary constructors, collection expressions, field keyword).
- **Naming**: Use PascalCase for methods and properties; prefix private fields with `this.` or `_`.
- **Quantower Patterns**: 
    - Always clean up event handlers (`Core.NewRequest`, etc.) in `OnStop()`.
    - Use `Log()` for diagnostic output within the Strategy logs.


## Reference Source & Dependencies
- **Quantower API Reference**: `C:\Users\Lex\repos\QuantowerRef`

# Git Guidelines
- Style: Intent-First (Imperative mood, no prefixes like 'fix:' or 'feat:').
- Length: Aim for 50 characters; absolute max 72.
- Format:
    - Subject: A single strong sentence starting with a verb.
    - Body: 1 to 5 bullet points explaining the "Why" and "How."
    - Example: "Widen base SL to reduce stop-outs during replay testing"
- Don't include Co-Authored-By footers in commit messages.
- Always preview the commit message before final execution.

# Text & Encoding Rules
- Strict ASCII Only: Do not use non-keyboard characters in code, comments, or commit messages.
- No Typography Glyphs: Avoid emojis, "smart" quotes, long dashes, and unicode arrows.
- Standard Substitutes: Use standard hyphens (-), double hyphens (--), and ASCII arrows (-> or =>) instead.

# Test Architecture Rules
- Smart Test Consolidation: Prefer xUnit [Theory] and [InlineData] for tests that share the exact same execution logic but differ only in input/output values.
- Maintain Test Clarity: Do not use [Theory] if the test requires complex branching logic or if statements inside the test body to handle different cases.
- Cohesion Requirement: Only combine tests when the underlying business requirement being validated is identical (e.g., different edge cases for the same validation rule).
- Separation of Concerns: Keep sign distinct behaviors (e.g., "Successful Order" vs. "Network Timeout") in separate methods, even if they share some setup code.

# E2E Log Review Rules
- When verifying e2e logs, derive expected values from first principles -- never from the code being tested.
- Work the math yourself from domain knowledge and settings inputs. If your independent result disagrees with the log, flag it -- even if the log is consistent with the code.
- If a formula includes an unexplained literal or term, flag it.

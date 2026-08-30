---
name: test-conventions
description: How tests are written in this project — the Given/When/Then naming scheme, TestData, NSubstitute for ports, FakeTimeProvider, NetArchTest. Use before writing or changing any test in tests/Entriqa.Tests, and whenever a story needs its failing proof.
---

# Test conventions

Tests live in `tests/Entriqa.Tests`, one file per subject. Every pairmode story starts
with a red test, so this is the first thing to get right.

## The naming scheme is absolute

`GivenPreconditions_WhenStateUnderTest_ThenExpectedBehavior` — **every test method in the project
follows it, without exception.** A name that does not is a mistake, not a style preference. Write the three parts as
readable English, long names are fine:

```
GivenAPathConfiguredForThatLocale_WhenBuildingTheConfirmPagePath_ThenTheConfiguredPathWins
GivenALeadMagnetWithoutFile_WhenCheckingBeforePublish_ThenBothStepsAreFlagged
```

The `Then` part states the behavior, never the mechanics — not `ThenCheckReturnsAList`.

## The tools, and what each is for

| Package | Used for |
|---|---|
| xunit | `[Fact]`, `[Theory]` + `[MemberData]`/`TheoryData` |
| NSubstitute | every port (`ISendTransactionalMailPort`, `IUpsertBrevoContactPort`, …) — ports are the seam, never fake the domain |
| `Microsoft.Extensions.TimeProvider.Testing` | `FakeTimeProvider`; time is pinned, never `DateTimeOffset.Now` |
| NetArchTest.Rules | the layering rules in `ArchitectureTests` |

The test project references Application, Data, Infrastructure and — since #11 — `Entriqa.Admin`,
but **not Functions** (the worker SDK does not load in the test host). Functions' layering is enforced by project references
instead; see the comment at the top of `ArchitectureTests`.

## TestData is the entry point

`TestData` (internal static) holds the shared fixtures. Reach for it before building anything:

- `TestData.Time` — `FakeTimeProvider` fixed at `2026-08-21T10:00:00Z`
- `TestData.Options()` — a valid `EntriqaOptions` (token secret long enough, `BaseUrl`, `SiteName`)
- `TestData.Tokens()` — a `FormTokenService` on that clock
- `TestData.Contact()` — a complete valid `FormDefinition`; vary it with `with { … }` rather than writing a new one
- `TestData.Quiz()` — a valid `QuizDefinition` with a jump and three results
- `TestData.Json("""{ … }""")` — a `JsonElement` for step configuration
- `TestData.AdminSubmissionList()` — 24 admin `SubmissionListItem`s across two forms, newest first,
  every quick filter non-empty and long enough for a second page. Named for its layer: the domain has
  a `SubmissionListItem` of its own

A record's `with` expression is the idiom for "valid form, but one thing wrong":

```csharp
var form = TestData.Contact() with { Pipeline = new[] { … } };
```

## Assembling a pipeline for a test

The fiddliest part, and the reason to copy rather than invent. `PublishCheckService` and
`SubmissionPipelineService` both need the step registry, and every step needs its own
substituted ports:

```csharp
var mail = Substitute.For<ISendTransactionalMailPort>();
var steps = new ISubmissionStep[] { new NotifyMailStep(mail), new DoiRequestStep(mail, TestData.Tokens()), … };
var pipeline = new SubmissionPipelineService(steps, Options.Create(TestData.Options()), TestData.Time,
                                             NullLogger<SubmissionPipelineService>.Instance);
```

Register **only the steps the test needs** — `SeedFormsTests` registers all nine because it
checks shipped definitions, `FormDefinitionRobustnessTests` registers one. An unregistered key
surfaces as a check issue ("unbekannter Schritt"), not an exception, so a short registry can
mask a typo.

## What the architecture tests enforce

Dependencies point inwards only: Domain depends on nothing, Application not on Data or
Infrastructure, Data not on Infrastructure, and entity types stay internal to the Data layer.
A layering mistake fails the gate — it is not something a reviewer has to catch.

## The admin *does* load — measure before you conclude it cannot

`Entriqa.Admin` is a Blazor WASM project, and for a long time the tests here said its assembly does
not load in this test host. **That was never measured, and it is false.** Since #11 the test project
references it, and its plain service types load and run like any other. So logic in `Services/*.cs`
gets ordinary executing tests: `SubmissionSelectionTests` is the example. Put the decidable part of a
page *there* rather than in the markup, and it is testable.

Component *types* load too, but nothing renders one — there is no bUnit here. Do not read this as an
invitation to write component tests; markup still needs the source guards below.

`Entriqa.Functions` is a different matter and deliberately stays unreferenced — the worker SDK does
not load here.

## What only lives in markup still gets a source guard

Some things have no runtime surface even with the admin referenced: whether a row takes focus,
whether a component binds a `Changed` callback, whether a dictionary initialiser lists a key twice
(at runtime the later entry has simply won). Read the **source** for those — it is not a reason to
write "acceptance-only". `EditorChangedBindingTests`, `AdminTranslationKeyTests` and
`SubmissionListRowTests` do exactly that, and `LocaleSourceGuardTests` follows them: resolve the
directory upwards from `AppContext.BaseDirectory`, pull out the member you care about with a regex,
and assert what must and must not be in it.

**Find the end of a Razor tag outside quotes.** An event handler in the tag is a lambda, so a scan
to the first `>` stops inside `() => Open(…)` and silently drops every attribute written after it —
the guard then passes while reading half a tag. `EditorChangedBindingTests` and
`SubmissionListRowTests` both track the quote state instead.

The pull is what makes it a guard rather than a grep — scanning the whole file matches the string
anywhere, including in a comment. Anchor patterns that can be commented out (`^\s*services\.Add…`
with `RegexOptions.Multiline`), and fail loudly when the member is gone (`Assert.True(match.Success,
"… no longer has X — update this guard.")`) rather than passing on an empty match.

It is weaker than executing the code and it does not replace the acceptance gate: it catches the
regression, not the defect. But "the admin does not load in the test host" has three times been the
premise of a conclusion that nothing could be guarded — twice the review found a mutation that
restored the pre-fix behaviour with the whole suite green, and the third time the premise itself
turned out to be wrong.

## A double answers the request, it does not replay a script

A fake `HttpMessageHandler` that hands out prepared responses in call order makes every client look
correct, including one that never varies its request. That is not hypothetical: the paging tests for
#22 served pages by call order, so replacing `offset={all.Count}` with `offset=0` left all 117 tests
green while the real endpoint would have returned page one forever. Four more mutants survived in the
same blind spot.

Model the endpoint instead — slice the data by the `limit` and `offset` you were asked for, and
report the total the API would report:

```csharp
private sealed class FakeHandler(string arrayName, long? claimedCount, int available) : HttpMessageHandler
{
    public List<string> Requests { get; } = new();
    // ... parse limit/offset from the query, return that slice, empty past the end
}
```

And assert on what was actually sent (`Assert.Equal(new[] { "…offset=0", "…offset=50" }, handler.Requests)`)
wherever a request parameter drives the behaviour under test. A count of requests is the right
assertion for termination; the URLs are the right assertion for correctness.

## Two traps

**`TheoryData` with file names, not paths.** `SeedFormsTests` passes `kontakt.json`, not the
absolute path, so the test name stays readable in the runner output. The directory is resolved
inside the test.

**Resolve directories upwards from `AppContext.BaseDirectory`.** The test binary runs in
`bin/Debug/net10.0`, so a path relative to the repository root does not exist. `SeedFormsTests`
walks up looking for `seed/forms`, exactly as `DevSeedHostedService` does in production. Copy
that helper rather than hard-coding `../../../..`.

## Running them

`node scripts/verify.mjs` runs everything. While iterating:

```
dotnet test Entriqa.slnx -c Debug --nologo --filter "FullyQualifiedName~QuizEngineTests"
```

Evidence for pairmode only counts through `verify-run.mjs` — see [`local-dev`](../local-dev/SKILL.md)
and `CLAUDE.md`.

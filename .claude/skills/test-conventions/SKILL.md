---
name: test-conventions
description: How tests are written in this project — the Given/When/Then naming scheme, TestData, NSubstitute for ports, FakeTimeProvider, NetArchTest. Use before writing or changing any test in api/tests, and whenever a story needs its failing proof.
---

# Test conventions

94 tests live in `api/tests/Entriqa.Tests`, one file per subject. Every pairmode story starts
with a red test, so this is the first thing to get right.

## The naming scheme is absolute

`GivenPreconditions_WhenStateUnderTest_ThenExpectedBehavior` — **84 of 84 test methods follow
it.** A name that does not is a mistake, not a style preference. Write the three parts as
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

The test project references Application, Data and Infrastructure — **not Functions** (the worker
SDK does not load in the test host). Functions' layering is enforced by project references
instead; see the comment at the top of `ArchitectureTests`.

## TestData is the entry point

`TestData` (internal static) holds the shared fixtures. Reach for it before building anything:

- `TestData.Time` — `FakeTimeProvider` fixed at `2026-08-21T10:00:00Z`
- `TestData.Options()` — a valid `EntriqaOptions` (token secret long enough, `BaseUrl`, `SiteName`)
- `TestData.Tokens()` — a `FormTokenService` on that clock
- `TestData.Contact()` — a complete valid `FormDefinition`; vary it with `with { … }` rather than writing a new one
- `TestData.Quiz()` — a valid `QuizDefinition` with a jump and three results
- `TestData.Json("""{ … }""")` — a `JsonElement` for step configuration

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
dotnet test api/Entriqa.sln -c Debug --nologo --filter "FullyQualifiedName~QuizEngineTests"
```

Evidence for pairmode only counts through `verify-run.mjs` — see [`local-dev`](../local-dev/SKILL.md)
and `CLAUDE.md`.

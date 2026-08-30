---
name: processing-pipeline
description: How submission post-processing works — step contract, phases and double opt-in, inline vs deferred, critical failures, publish checks. Use when adding or changing a pipeline step, touching the double opt-in flow, or working on any area:api issue about notifications, CRM, PDFs, Teams or webhooks.
---

# The processing pipeline

What happens after a visitor submits. Nine steps, one runner, ~850 lines — and the densest
implicit logic in the product. Adding a step is easy; getting its *position* and *mode* right
is where mistakes happen.

Runner: `src/Core/Entriqa.Application/Pipeline/SubmissionPipelineService.cs`
Contract: `ISubmissionStep.cs` · Check rules: `PublishCheckService.cs` · Steps: `Pipeline/Steps/`

## The steps

| Key | Does | Needs |
|---|---|---|
| `notify.mail` | Notification to the operator | — |
| `doi.request` | Double opt-in mail — **splits the phases** | email + consent field |
| `brevo.contact` | Contact into the CRM and its lists | |
| `brevo.company` | Company record; requires a `brevo.contact` step | |
| `brevo.mail` | Mail to the participant, can attach an artifact | |
| `leadmagnet.link` | Time-limited download link | |
| `reportingcloud.pdf` | Generates a PDF (**deferred**) | |
| `teams.notify` | Teams card | |
| `webhook.call` | Calls an external endpoint | |

Adding a step really is just "write the class": DI registers everything with the `Step` suffix,
and the admin reads the catalogue through `StepCatalogService`.

## The four mechanics that go wrong

### 1. Phases — everything after `doi.request` waits for the click

`SplitsPhase` is true for `doi.request` and nothing else. `CreateRuns` walks the pipeline and
assigns `StepPhase.OnSubmit` until it passes that step, `StepPhase.OnConfirm` afterwards. A
step in the confirm phase is set to `Waiting` while the submission is unconfirmed.

This is why **order is a legal matter, not a preference**. `PublishCheckService` blocks a
`brevo.contact` that has no `doi.request` before it, with a message spelling out why: addresses
would land in a marketing list unconfirmed. Only a form that collects no marketing consent at
all is a deliberate exception.

Two `doi.request` steps are rejected — one confirmation per submission.

### 2. Inline vs deferred — a deferred step ends the run

`StepMode.Deferred` does **not** mean "this one step runs later". When the runner meets a
deferred step during an inline run it `break`s out of the loop entirely, and everything from
there on runs in the deferred pass.

The reason is in the code and worth keeping in mind: otherwise an inline consumer would run
before its deferred producer and never see the artifact — `brevo.mail` with `attach: "report"`
placed after `reportingcloud.pdf` is the concrete case. `RunAsync` returns `true` when deferred
work is left, which is what gives the client its run token.

### 3. Critical vs non-critical — and `Blocked` is not `Failed`

`StepDefinition.Critical` (per form) overrides `ISubmissionStep.CriticalByDefault` (per step).
Notifications default to non-critical: their failure must not stop the CRM entry.

When a critical step fails, the runner sets `broken` and every following step becomes
**`Blocked`**, not `Failed`. The distinction matters for retries: `RunMode.Retry` resets failed
*and* blocked runs to `Pending` before running, so fixing the cause re-runs the whole tail.

An exception inside a step is caught, trimmed to 500 characters and stored on the run —
`OperationCanceledException` is deliberately not caught.

### 4. `Needs` / `Produces` / `CheckConfig` — the contract with the publish check

A step declares what it requires and what it leaves behind:

- `Needs` → `StepNeed.EmailField` / `ConsentField`; the check reports a form that lacks them.
- `Produces` → an artifact name (`"report"`, `"download"`) put into `Submission.Artifacts`.
- `CheckConfig(config, form, producedBefore)` → problems in plain words. `producedBefore` holds
  the artifacts of *earlier* steps, which is how `brevo.mail` verifies that the PDF it wants to
  attach is actually produced before it runs.

Read configuration **only** through the `StepConfig` extensions (`GetString`, `GetInt`,
`GetIntList`, `GetStringMap`). They return null or empty for the wrong `ValueKind` instead of
throwing, which is what lets `CheckConfig` report rather than crash.

**A field whose result reaches the visitor takes a value per language** (#3). Mark it
`"localizable": true` in the `ConfigSchema` and read it through the `(name, locale)` overloads —
`config.GetInt("templateId", ctx.Submission.Locale)`. The stored value is a plain scalar, which
applies to every language, or `{locale: value}`; `LValue` in the domain holds that rule and its
`LText`-identical fallback to the first entry present. Marked leaves are never object-typed, which
is what makes an object unambiguously a locale map.

Two traps, both of which have already cost a defect:

- **The 2-arg overloads are wrong for a marked field**, not merely less convenient. Against the
  object form they return null/empty, so `CheckConfig` reports "nothing configured" and
  `ExecuteAsync` dereferences null. Move both the execution *and* the check when you mark a field.
- **A map whose *members* are localizable** — `reportingcloud.pdf`'s per-result `templates` — marks
  `additionalProperties`, not the property. Read the map with `GetRaw` (resolving the map itself
  would hand back an arbitrary member) and each member through `GetStringMap(name, locale)`.

The marker is also what drives the publish check: `PublishCheckService.MissingConfigLocales` refuses
any marked field that carries a value but not one for every declared language, naming step, field
and language. A step that needs a message of its own — because only it knows what the keys mean —
writes it in `CheckConfig`, as `reportingcloud.pdf` does per quiz result.

The builder-side duplication of the collapse rule is tracked in #44.

`ConfigSchema` is JSON Schema and drives the builder UI. `MailParams` are the `{{ params.… }}`
placeholders the admin shows as help for the mail template — declare them or nobody can wire
the template up.

## Conditions

`StepDefinition.When`: `always`, `hasEmail`, or `result:{resultId}` for quiz outcomes. An
unrecognised condition evaluates to false — the step is skipped, silently. Add new conditions in
`ConditionMet` and `StepConditions` together.

## Writing a new step

1. Class in `Pipeline/Steps/`, suffix `Step`, implementing `ISubmissionStep`. DI picks it up.
2. Pick `Mode` deliberately — deferred splits the run (see 2).
3. `CriticalByDefault = false` only if a failure genuinely must not block what follows.
4. Declare `Needs`, `Produces`, `ConfigSchema`, `MailParams`.
5. `CheckConfig` for everything that can be misconfigured — a check issue beats a runtime failure.
6. Tests: see [`test-conventions`](../test-conventions/SKILL.md) for assembling the registry.

## Never call the real services

The CRM host, production Azure Storage and the Teams webhook hosts are blocked in
`pairmode.config.json`, and the bash guard enforces it. Locally the keys stay empty, and mails
land as HTML files — see [`local-dev`](../local-dev/SKILL.md). A dev loop against the live CRM
creates real contacts and sends real mail.

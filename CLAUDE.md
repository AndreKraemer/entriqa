# Entriqa — Project Baseline

Self-hosted form and submission system for Hugo websites on Azure Static Web Apps: forms,
quizzes, uploads and a configurable processing pipeline (email, Brevo, double opt-in, PDF,
Teams, webhook), administered through a Blazor WASM admin.

The process (story → plan → red test → implementation → review → PR) lives in the pairmode
plugin. This file holds only what is specific to this project.

## Hard rules

- **Language: English everywhere** — code, comments, test names, commit and PR text,
  documentation, specs, and GitHub issues. Tests follow Given/When/Then. The exceptions are
  texts an end user or operator reads: `hugo/content/`, the admin UI strings in
  `Ui.cs` (whose German strings double as the translation keys), `ValidationMessages`, the German
  branch of `ErrorMessages`, the publish-check findings in `PublishCheckService` and
  `QuizEngine.Check`, the log messages, and the console output of `dev/start.mjs`. `README.md`
  and `LICENSE.md` stay German too — the licence because translating its terms has legal effect.
  Everything else, comments in build files included, is English. Issues #8–#23 and the German
  code comments predate the rule (see #27).
- **No live endpoints** from tests or dev loops: Brevo, production Azure Storage and Teams
  webhooks are blocked in `pairmode.config.json`. Azurite runs locally.
- **No browser storage and no cookies** for the website visitor (§ 25 TDDDG) — this is a
  product promise, not an implementation detail. See README.
- **Build settings live at the repository root** — `Directory.Build.props`,
  `Directory.Packages.props`, `global.json`, `nuget.config`, `.editorconfig`. Every project
  inherits them: `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`,
  `EnforceCodeStyleInBuild` and central package versions. A warning is an error. Analyzer rules
  that were deliberately downgraded carry their reason in `.editorconfig`; the test project
  relaxes `CA1707` because its `Given_When_Then` naming needs underscores.
- **`dotnet test` does not build the whole solution** — only what the test project depends on.
  The gate therefore builds `Entriqa.slnx` first, or a broken admin slips through.
- **Commit subjects are plain imperative sentences** — `Drop the quiz and source attributes from
  the Brevo contact step (#23)`, never a Conventional-Commits prefix like `feat:`/`fix:`/
  `refactor:`. The issue goes in parentheses at the end. This holds when a tool or command
  suggests a prefix: the repository's own history is the yardstick, and a review measures
  against it. **One exception**: pairmode's red-state commit `test: red tests + plan for #N
  (<title>)` keeps its prefix — that subject is a marker read by code (`stop-gate.mjs` matches
  `/^test: red tests \+ plan/`, `/pairmode:implement` names it as a precondition), so it is a
  trailer that happens to sit in the subject line, not a style choice.

## Layout

Follows the QB .NET Solution Standard v0.5 §6.1 (Stage 1), with the non-.NET parts of the
product alongside it. Solution folders in `Entriqa.slnx` mirror the physical ones, except
`20 Build & Deploy`, which gathers four (§6.2 asks for one each).

| Folder | Contents |
|---|---|
| `src/Hosts/` | `Entriqa.Functions` (Azure Functions isolated, composition root) and `Entriqa.KeyTool` (licence tool, vendor only) |
| `src/Core/` | `Entriqa.Domain`, `.Application`, `.Data`, `.Infrastructure` — Clean Architecture, dependencies point inwards |
| `src/Ui/` | `Entriqa.Admin`, the Blazor WASM admin served under `/admin` |
| `tests/` | `Entriqa.Tests` — never under `src/` |
| `hugo/` | Hugo module: shortcode, embed partial, forms.js/css, i18n, DOI pages. **Markdown here is product, not documentation.** |
| `samples/site/` | Bilingual Hugo site importing the module; `dev/start.mjs` defaults to it. Covers every field type and all three form types |
| `seed/forms/` | The form definitions the sample site embeds; published at startup by `DevSeedHostedService` |
| `scripts/`, `dev/` | The verify gate and the local dev launcher |
| `docs/`, `pipelines/` | Documentation and the customer pipeline template |

Deliberate deviations from the standard, as complete as it is currently known:

- no Aspire `AppHost`/`MigrationService` — no relational database (Azure Tables), and
  `dev/start.mjs` fills that role;
- no `src/Modules` — Stage 2 needs ≥ 4 developers (§5.2);
- `hugo/`, `seed/`, `samples/` have no counterpart in a pure .NET layout;
- no `/build` or `/deploy`; CI lives in `.github/workflows/` (GitHub requires that path) and the
  gate in `scripts/`;
- one flat `tests/Entriqa.Tests` instead of a mirror of `/src`, and no separate
  `*.ArchitectureTests` project — the architecture tests are a class inside it;
- xUnit v2 on `Microsoft.NET.Test.Sdk`, not xUnit v3 on Microsoft Testing Platform (§22.1);
- still missing and tracked as debt: `docs/adr/` with a first ADR, an E2E smoke, and
  `build/Version.Build.props` (§4, §21.3).

## Verify

| Purpose | Command |
|---|---|
| Fast gate (every change) | `node scripts/verify.mjs` |
| Full gate (before the PR) | `node scripts/verify.mjs --full` |
| Start the local environment | `npm run dev` |
| Build the sample site alone | `npm run sample:build` |

The fast gate builds `Entriqa.slnx` in Debug, runs all tests against that build, and checks the
`eq-*` class reference against `forms.js` (`scripts/check-eq-classes.mjs`). It does **not** build
the sample site — that needs Hugo and Go, which the release workflow does not have; run
`npm run sample:build` by hand after touching `hugo/` or `samples/site/`. The full gate does the
same in Release and additionally runs the two publishes that `release.yml` performs on a tag
build. Exit 0 = PASS.

Note: a run only counts as evidence when it goes through pairmode's `verify-run.mjs`, which
records the fingerprint of the working tree. Calling `scripts/verify.mjs` directly is fine
for a quick check but leaves no proof behind.

**Run it after committing, not before.** The fingerprint changes with the commit, so a green run
from just before it no longer matches the tree and the stop-gate asks for another one. Verify
while iterating as often as you like; the run that counts is the one on the committed state.

## Skills

Registered in the `skills` map of `pairmode.config.json`:

| Skill | Covers |
|---|---|
| [`local-dev`](.claude/skills/local-dev/SKILL.md) | Starting, driving and debugging the app: ports, the bundled sample site, seeding, dev mails, local auth |
| [`test-conventions`](.claude/skills/test-conventions/SKILL.md) | Given/When/Then naming, `TestData`, NSubstitute on ports, `FakeTimeProvider`, the architecture tests |
| [`processing-pipeline`](.claude/skills/processing-pipeline/SKILL.md) | Step contract, DOI phases, inline vs deferred, critical vs blocked, publish checks |
| [`admin-ui`](.claude/skills/admin-ui/SKILL.md) | German strings as translation keys, the form builder, the schematic preview, the `eq-*` contract |

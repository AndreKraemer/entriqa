# Entriqa — Project Baseline

Self-hosted form and submission system for Hugo websites on Azure Static Web Apps: forms,
quizzes, uploads and a configurable processing pipeline (email, Brevo, double opt-in, PDF,
Teams, webhook), administered through a Blazor WASM admin.

The process (story → plan → red test → implementation → review → PR) lives in the pairmode
plugin. This file holds only what is specific to this project.

## Hard rules

- **Language: English everywhere** — code, comments, test names, commit and PR text,
  documentation, README, specs, and GitHub issues. Tests follow Given/When/Then. The one
  exception is product text shipped to German site visitors: `hugo/content/`, the admin UI
  strings in `Ui.cs` (whose German strings double as the translation keys),
  `ValidationMessages`, the German branch of `ErrorMessages`, and the console output of
  `dev/start.mjs`. `LICENSE.md` also stays German — translating licence terms has legal
  effect. Issues #8–#23 predate the rule and are still German (see #27).
- **No live endpoints** from tests or dev loops: Brevo, production Azure Storage and Teams
  webhooks are blocked in `pairmode.config.json`. Azurite runs locally.
- **No browser storage and no cookies** for the website visitor (§ 25 TDDDG) — this is a
  product promise, not an implementation detail. See README.
- `TreatWarningsAsErrors` is on project-wide; a warning is an error.
- **`admin/` is not part of `api/Entriqa.sln`** but references `Entriqa.Domain`. Anyone
  touching the domain has to build the admin too — the gate does that.

## Layout

| Folder | Contents |
|---|---|
| `hugo/` | Hugo module: shortcode, embed partial, forms.js/css, DOI pages. **Markdown here is product, not documentation.** |
| `api/` | .NET 10, Azure Functions isolated, Clean Architecture (Domain/Application/Data/Infrastructure/Functions) |
| `admin/` | Blazor WASM admin served under `/admin` |
| `dev/` | `node dev/start.mjs` starts Azurite + SWA CLI locally |
| `samples/site/` | Bilingual Hugo site importing the module; `dev/start.mjs` defaults to it. Covers every field type and all three form types |
| `seed/forms/` | The form definitions the sample site embeds; published at startup by `DevSeedHostedService` |
| `docs/`, `pipelines/`, `tools/` | Documentation, customer templates, licensing tool |

## Verify

| Purpose | Command |
|---|---|
| Fast gate (every change) | `node scripts/verify.mjs` |
| Full gate (before the PR) | `node scripts/verify.mjs --full` |
| Start the local environment | `npm run dev` |
| Build the sample site alone | `npm run sample:build` |

The fast gate builds and tests `api/` in Debug and builds the admin. It does **not** build
the sample site — that needs Hugo and Go, which the release workflow does not have; run
`npm run sample:build` by hand after touching `hugo/` or `samples/site/`. The full gate does the
same in Release and additionally runs the two publishes that `release.yml` performs on a tag
build. Exit 0 = PASS.

Note: a run only counts as evidence when it goes through pairmode's `verify-run.mjs`, which
records the fingerprint of the working tree. Calling `scripts/verify.mjs` directly is fine
for a quick check but leaves no proof behind.

## Skills

Registered in the `skills` map of `pairmode.config.json`:

| Skill | Covers |
|---|---|
| [`local-dev`](.claude/skills/local-dev/SKILL.md) | Starting, driving and debugging the app: ports, the bundled sample site, seeding, dev mails, local auth |
| [`test-conventions`](.claude/skills/test-conventions/SKILL.md) | Given/When/Then naming, `TestData`, NSubstitute on ports, `FakeTimeProvider`, the architecture tests |
| [`processing-pipeline`](.claude/skills/processing-pipeline/SKILL.md) | Step contract, DOI phases, inline vs deferred, critical vs blocked, publish checks |
| [`admin-ui`](.claude/skills/admin-ui/SKILL.md) | German strings as translation keys, the form builder, the schematic preview, the `eq-*` contract |

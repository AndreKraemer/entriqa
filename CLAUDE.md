# Entriqa — Project Baseline

Self-hosted form and submission system for Hugo websites on Azure Static Web Apps: forms,
quizzes, uploads and a configurable processing pipeline (email, Brevo, double opt-in, PDF,
Teams, webhook), administered through a Blazor WASM admin.

The process (story → plan → red test → implementation → review → PR) lives in the pairmode
plugin. This file holds only what is specific to this project.

## Hard rules

- **Language: everything that lands in the repository is English** — code, comments, test
  names, commit and PR text, documentation, README and specs. Tests follow Given/When/Then.
  The two exceptions are product text shipped to German site visitors (`hugo/content/`,
  admin UI strings, localized API messages) and GitHub issues, which live outside the repo.
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
| `seed/`, `docs/`, `pipelines/`, `tools/` | Sample data, documentation, customer templates, licensing tool |

## Verify

| Purpose | Command |
|---|---|
| Fast gate (every change) | `node scripts/verify.mjs` |
| Full gate (before the PR) | `node scripts/verify.mjs --full` |
| Start the local environment | `node dev/start.mjs` |

The fast gate builds and tests `api/` in Debug and builds the admin. The full gate does the
same in Release and additionally runs the two publishes that `release.yml` performs on a tag
build. Exit 0 = PASS.

Note: a run only counts as evidence when it goes through pairmode's `verify-run.mjs`, which
records the fingerprint of the working tree. Calling `scripts/verify.mjs` directly is fine
for a quick check but leaves no proof behind.

## Skills

No project skills created yet (`skills` in `pairmode.config.json` is empty). Candidates,
ordered by expected value:

| Area | Why |
|---|---|
| Processing pipeline | Brevo, double opt-in, PDF, Teams, webhook — the densest domain logic in the product |
| Test conventions | 71 tests exist; the convention is written down nowhere |
| Admin/UI design | `eq-*` classes, form builder, live preview DE/EN |
| Running & debugging locally | Azurite + SWA CLI; a prerequisite for `/pairmode:acceptance` |

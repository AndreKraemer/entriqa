---
name: local-dev
description: Run and debug Entriqa locally — Azurite, the Functions API, the Blazor admin, Hugo and the two SWA CLI proxies. Use when starting the app, reproducing behavior in a browser, checking a form end to end, inspecting dev mails, or running /pairmode:acceptance.
---

# Running Entriqa locally

`node dev/start.mjs --site <path-to-a-hugo-site>` starts everything. The single most
important fact: **the Hugo site is not in this repository.** This repo ships the Hugo
*module*; you need a separate Hugo site that imports it. Without `--site` the script falls
back to `ENTRIQA_SITE_ROOT` and then to the current directory, and exits with
"Keine Hugo-Site gefunden" if neither looks like one (no `hugo.toml`/`hugo.yaml`/`config.toml`).

The script rewrites the site's module import via `HUGO_MODULE_REPLACEMENTS`
(`github.com/andrekraemer/entriqa/hugo` → this clone), so edits under `hugo/` take effect
immediately without publishing a module version.

## What comes up

| Port | What | Notes |
|---|---|---|
| 4280 | Website (SWA proxy → Hugo) | **Use this, not 1313** — Hugo runs with `--baseURL http://localhost:4280/`, so absolute asset URLs only resolve through the proxy |
| 4281 | Admin (SWA proxy → Blazor) | Sign in at `/.auth/login/aad`, add the role `admin` in the emulator's form |
| 7071 | Functions API | Both proxies point their `/api/*` here |
| 1313 | Hugo dev server | Direct access breaks icon fonts and mask SVGs (CORS) |
| 5100 | Blazor dev server | Direct access bypasses SWA auth |
| 10000-10002 | Azurite | Blob/Queue/Table, data under `dev/.azurite` |

Startup is staggered: Azurite first (the script waits for
`http://127.0.0.1:10002/devstoreaccount1` — the API's dev seed fails without it), then API,
admin and Hugo, then the two proxies after 5s. Give it ~10s before the summary block prints.

## Prerequisites the script checks

It preflights and exits with a per-tool hint: .NET SDK, Azure Functions Core Tools v4, Hugo
(extended), Go (for Hugo modules). `hugo`, `azurite` and `swa` are also looked up in the
`node_modules/.bin` of both the site and this repo, so `npm install` here covers two of them.
On Windows the Core Tools are found inside the Visual Studio installation
(`%LOCALAPPDATA%\AzureFunctionsTools\Releases`) — no separate install needed.

## Seed data

`Entriqa__SeedFolder` is `seed/forms` in `local.settings.json`. On startup in Development,
`DevSeedHostedService` publishes every `seed/forms/*.json` whose slug is not published yet —
currently just `kontakt.json`. It **skips** forms that already exist, so editing a seed file
does nothing until you delete the form in the admin or wipe `dev/.azurite`. That is the usual
reason a seed change "does not show up".

## Mails and outbound calls

Without a Brevo API key, mails are written as clickable HTML files to
`<TEMP>/entriqa-devmails/` (the path is printed at startup). Open them from there — this is
also how to verify double opt-in without touching the CRM.

Leave `Entriqa__Brevo__ApiKey` and `Entriqa__ReportingCloud__ApiKey` empty.
`pairmode.config.json` blocks the Brevo host, `*.core.windows.net` and the Teams webhook
hosts, and that block exists because a dev loop against live Brevo creates real contacts and
sends real mail.

## Auth locally

`Entriqa__AllowAnonymousAdmin` is `true` in `local.settings.json`, so the API accepts admin
calls without a principal. The SWA proxy on 4281 still gates the UI: you have to go through
`/.auth/login/aad` and type `admin` into the roles field of the emulator's login form. Hitting
5100 directly skips that gate entirely — useful for a quick UI check, misleading if you are
testing anything authorization-related.

## Resetting

Stop with Ctrl+C (all children are killed with the parent). To get back to a clean slate,
delete `dev/.azurite` — that drops all forms, submissions and contacts, and the next start
re-seeds `seed/forms`.

## For /pairmode:acceptance

Drive port 4280 for anything a site visitor does and 4281 for the admin. Evidence should come
from those two origins; a result taken from 1313 or 5100 does not prove the real routing,
auth or API wiring works. `docs/staticwebapp.config.json` is the routing contract the
proxies emulate — `/admin/*` and `/api/manage/*` require the `admin` role, `/f/*` rewrites to
the DOI page.

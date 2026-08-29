---
name: local-dev
description: Run and debug Entriqa locally — Azurite, the Functions API, the Blazor admin, Hugo and the two SWA CLI proxies. Use when starting the app, reproducing behavior in a browser, checking a form end to end, inspecting dev mails, or running /pairmode:acceptance.
---

# Running Entriqa locally

`npm run dev` (or `node dev/start.mjs`) starts everything. With no `--site` argument it uses
the bundled sample site in `samples/site`, so a fresh clone runs with nothing external; the
order is `--site <path>`, then `ENTRIQA_SITE_ROOT`, then the bundled site. It exits with
"Keine Hugo-Site gefunden" if a given path does not look like a site (no
`hugo.toml`/`hugo.yaml`/`config.toml`).

The script rewrites the site's module import via `HUGO_MODULE_REPLACEMENTS`
(`github.com/andrekraemer/entriqa/hugo` → this clone), so edits under `hugo/` take effect
immediately without publishing a module version. That path must be **absolute** — Hugo does
not resolve a relative module replacement against the project directory on Windows and fails
with "module does not exist". `scripts/hugo.mjs` (`npm run sample:build` / `sample:serve`)
does the same for the sample site without Azurite and the API.

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
`kontakt`, `beratung`, `whitepaper` and `selbsttest`, which the sample site embeds one per
page. It **skips** forms that already exist, so editing a seed file
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

## Auth locally — measure it, do not assume it

What is gated locally is narrower than it looks, and the difference has already misled one
acceptance report. Measured on the branch, with no login anywhere:

| Request | Result |
|---|---|
| `4281/`, `4281/einsendungen` | **200** — the admin UI shell is served |
| `4281/api/manage/*` | 302 → `/.auth/login/aad` |
| `4280/admin/` | 302 → `/.auth/login/aad` |
| `7071/api/manage/*` | 200, **data in plain text** |

Three separate mechanisms produce that table, and confusing them is the trap:

- **The route rules gate the API, not the local admin UI.** `staticwebapp.config.json` protects
  `/admin/*`, and in production the admin lives there. Locally it is served at the *root* of the
  second proxy, where no rule matches — so the shell comes through. The application notices and
  renders "Bitte zuerst anmelden …" instead of data, because every one of its calls is redirected.
  Tracked as issue #33; until it is fixed, **the redirect-before-load behaviour cannot be observed
  locally at all**, so never report it as verified from a local run.
- **Both proxies do get the configuration**, even though only the site proxy is passed
  `--swa-config-location`: they run with `cwd = samples/site` and the SWA CLI finds
  `static/staticwebapp.config.json` there. Before that file moved into the site there were no rules
  at all, `/api/manage/*` answered 200 unauthenticated, and the `/f/*` rewrite was missing.
- **`Entriqa__AllowAnonymousAdmin` is `true`**, so the API on 7071 serves admin data to anyone.
  That is a deliberate development setting and is unaffected by any proxy configuration — it is
  why port 7071 is a convenient way to read state during a test run, and why nothing measured
  there says anything about authorization.

To sign in: open `/.auth/login/aad` on 4281, pick a username (the field is mandatory — an empty
one blocks the submit with a validation bubble that is easy to miss), and type `admin` into the
roles field.

**Then check that the role actually took**, before concluding anything from what the admin shows:

```
/.auth/me                 → userRoles must contain "admin"
/api/manage/forms         → must answer 200, not redirect to /.auth/login/aad
```

Measured with SWA CLI 2.0.10: the login form **dropped the role**. `/.auth/me` kept returning
`["anonymous","authenticated"]` and every manage call redirected, while the admin shell still
loaded and rendered "Bitte zuerst anmelden …". That looks exactly like a correctly gated admin and
cost most of an acceptance run. Same result with the roles as three lines, as `admin` alone, after
the form's *Clear* button, and through the site proxy on 4280.

If the role does not stick, set the principal the way the emulator stores it — a base64 cookie,
not HttpOnly — in the browser console on 4281, then reload:

```js
document.cookie = "StaticWebAppsAuthCookie=" + btoa(JSON.stringify({
  userId: "local-dev", userRoles: ["anonymous", "authenticated", "admin"],
  claims: [], identityProvider: "aad", userDetails: "acceptance" })) + "; path=/";
```

That is the emulator simulating a login, not a bypass of a real control — but say so in an
acceptance report, because it is how the evidence was produced.

Hitting 5100 directly skips every layer, and it does **not** proxy `/api/*`: manage calls answer
`200` with the SPA fallback HTML, so the admin shows "Bitte zuerst anmelden …" there whoever you
are. Fine for a quick look at a component, useless for anything that needs data — and its "200 OK"
in a network log is a trap, not a success.

**The standalone `/f/{slug}/` route cannot be exercised through the dev server.** The rewrite
targets `/f/index.html`, and Hugo's dev server answers that with `301 → ./`, which loops back.
The file exists in a real build, so this is a dev-server artifact, not a product defect — verify
that route against a deployed build or `npm run sample:build` output, never locally.

## Stopping and resetting

Ctrl+C in the foreground kills the whole tree. **Killing the port listeners is not enough**: the
parent `node dev/start.mjs` survives and keeps its children, so the next start fails with
`EADDRINUSE` on 10000 and a socket error from the admin — which looks like a broken environment
rather than a leftover one. That cost an acceptance run. End the parent process, then confirm
every port has no listener before restarting:

```
netstat -ano | grep LISTENING | grep -E ':(4280|4281|7071|5100|1313|1000[0-2]) '
```

Entries in `TIME_WAIT` are harmless and clear by themselves — only a `LISTENING` line matters.

To get back to a clean slate, delete `dev/.azurite` — that drops all forms, submissions and
contacts, and the next start re-seeds `seed/forms` including the lead-magnet files. Empty
`<TEMP>/entriqa-devmails` too, or an older confirmation mail will be the newest file you find.

The API is ready later than the proxies: 4280 answers before the Functions host does. Wait for a
form to return 200 (`curl -o /dev/null -w '%{http_code}' http://localhost:4280/api/forms/kontakt`)
rather than for a line in the log.

## For /pairmode:acceptance

Drive port 4280 for anything a site visitor does and 4281 for the admin. Evidence should come
from those two origins; a result taken from 1313 or 5100 does not prove the real routing or API
wiring works. **Authorization is the exception** — see the auth section above: the admin UI is
outside the route rules locally, so a local run cannot show that the admin is gated. Say so in the
report rather than reporting it as verified. `samples/site/static/staticwebapp.config.json` is the routing contract the
proxies emulate — `/admin/*` and `/api/manage/*` require the `admin` role, `/f/*` rewrites to
the DOI page.

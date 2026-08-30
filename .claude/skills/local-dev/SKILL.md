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

**The same service also fills the inbox** (#11), and only when the store holds *no* submission at all —
`Seed: {Count} Demo-Einsendungen angelegt` is the line to look for. It writes 18 for `kontakt` and 6 for
`whitepaper`, newest first, so that every branch of the admin's list exists without submitting anything
by hand: a second page, a non-empty count on each quick filter, and **exactly one deliberately failed
run** ("Brevo antwortete mit 502 (Demo-Daten)"). That failure is seeded, not a broken environment — do
not chase it. One `kontakt` message carries a long unbroken URL, which is there to test that the detail
page never scrolls sideways.

Deleting submissions in the admin therefore re-seeds them on the next start. Wipe `dev/.azurite` for a
clean slate as before; there is no way to ask for an empty inbox short of editing the service.

## Mails and outbound calls

Without a Brevo API key, mails are written as clickable HTML files to
`<TEMP>/entriqa-devmails/` (the path is printed at startup). Open them from there — this is
also how to verify double opt-in without touching the CRM. **The file name carries the Brevo
template id** (`…-template6-someone_example.org.html`), which is how you tell which template a
step actually chose.

**Contact upserts land in the same folder, in `contacts.log`** — one line per contact with the
list ids and the attributes: `acceptance-en@example.org  Listen: [8]  {"SPRACHE":"en",…}`. So CRM
routing *is* observable locally, without ever calling Brevo. Issue #3 twice declared the opposite
("the endpoint is blocked, so that branch stays unit-level only") and the acceptance run disproved
it; the claim was wrong because this line was missing here, not because the evidence was.

**The Brevo directory is simulated too, and it is on by default.** With an empty key,
`Entriqa__Dev__BrevoDirectorySize` (120 in the tracked `local.settings.json`) serves that many
synthetic lists and templates to the step editor - otherwise there is nothing to pick from and the
selection cannot be exercised at all. **The lists you see locally are invented**, so never read them
as an account's real content in an acceptance report. `Entriqa__Dev__BrevoDirectoryFailure` set to
`lists` or `templates` makes that section fail to load, which is the only way to see what an
incompletely loaded directory looks like without breaking Brevo. Both are off in production: the
size defaults to 0, and a deployment without a key gets an empty directory, not a fake one.

Leave `Entriqa__Brevo__ApiKey` and `Entriqa__ReportingCloud__ApiKey` empty.
`pairmode.config.json` blocks the Brevo host, `*.core.windows.net` and the Teams webhook
hosts, and that block exists because a dev loop against live Brevo creates real contacts and
sends real mail.

## The application's own log lines never reach the console

Nothing the app logs through `ILogger` shows up — not in `npm run dev`, not in
`func start --verbose`, not in `dotnet run` on the Functions project. The Functions host prints its
own lines (`Executing 'Functions.GetForm'`, the route table, the host lock) and swallows the
worker's, so a log-based expectation silently has no evidence either way.

**Do not read the silence as "it did not run."** Establish a baseline first: `DevSeedHostedService`
logs `Seed: {Slug} veröffentlicht` for every form it publishes. If those lines are absent while the
four forms exist in the admin, the channel is what is missing, not the behaviour.

To actually see what a startup component logs, build a small console project **outside the repo**
that references the built assemblies and runs the component with a real logger:

```csharp
using var factory = LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));
var service = new LocaleWarningHostedService(Options.Create(options), factory.CreateLogger<LocaleWarningHostedService>());
await service.StartAsync(CancellationToken.None);
```

That is real evidence of the component, not of the wiring — pair it with a source guard on the
registration (see [`test-conventions`](../test-conventions/SKILL.md)), and say in the acceptance
report that the two together stand in for the line you could not observe.

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

**Fill the field with real keystrokes.** The emulator's login page persists each field to
`localStorage` on `keyup` only, and its submit builds the cookie from *that*, never from the DOM
(`node_modules/@azure/static-web-apps-cli/dist/public/auth.html`):

```js
form.on("keyup", "input, textarea", (event) => $(event.currentTarget).saveToLocalStorage());
function saveCookie(formElement) {
  const data = localStorage[hashStorageKey(formElement)];   // not the form's current values
  document.cookie = `StaticWebAppsAuthCookie=${btoa(data)}; path=/`;
}
```

So a field filled by script, by `insertText`-style automation, or by paste — anything that fires no
keyup — leaves `localStorage` at its previous state, and the login then succeeds *with the old
principal*: the page redirects, the admin shell loads, and `admin` is simply missing. That looks
exactly like a correctly gated admin and cost most of an acceptance run. The form's *Clear* button
does not rescue it either: it calls `saveToLocalStorage` on every field, storing the **default**
roles.

If the role still does not stick, write the principal yourself — which is precisely what
`saveCookie` does — in the browser console on 4281, then reload:

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

**That check is necessary but not sufficient.** A worker can survive without holding any port and
still answer through a proxy the next start brings up — so the ports look free, the environment looks
fresh, and the API serves the previous session's code. That happened: the first directory query of an
acceptance run came back empty and read as a configuration defect, until the culprit turned out to be
an orphaned `dotnet.exe` running `Entriqa.Functions.dll` from the session before. `ps` under Git Bash
does not list it — it only shows that shell's own descendants. Ask Windows directly:

```powershell
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match 'entriqa' -and $_.Name -match 'node|dotnet|func' } |
  Select-Object ProcessId, Name, CommandLine
```

Kill what it lists, then repeat until it is empty — killing the host can leave the worker behind, and
killing the worker can leave a rebuilt one. Only then start again.

To get back to a clean slate, delete `dev/.azurite` — that drops all forms, submissions and
contacts, and the next start re-seeds `seed/forms` including the lead-magnet files. Empty
`<TEMP>/entriqa-devmails` too, or an older confirmation mail will be the newest file you find.

The API is ready later than the proxies: 4280 answers before the Functions host does. Wait for a
form to return 200 (`curl -o /dev/null -w '%{http_code}' http://localhost:4280/api/forms/kontakt`)
rather than for a line in the log.

**After rebuilding the admin, an open browser tab is poison.** The symptom is a wall of
`Failed to find a valid digest in the 'integrity' attribute`, `SRI's integrity checks failed` and 404s
on names like `Entriqa.Domain.vccoacpn42.wasm` — it reads as a broken build, and it is not. Blazor
fingerprints every assembly and pins it with SRI; a rebuild changes the names, and a tab holding the
old `index.html` asks for files that no longer exist. Clear that tab's cache (DevTools open → right
click on reload → *Cache leeren und vollständig neu laden*, or Application → Clear site data), or use
a fresh window. Nothing on the server needs fixing.

## For /pairmode:acceptance

Drive port 4280 for anything a site visitor does and 4281 for the admin. Evidence should come
from those two origins; a result taken from 1313 or 5100 does not prove the real routing or API
wiring works. **Authorization is the exception** — see the auth section above: the admin UI is
outside the route rules locally, so a local run cannot show that the admin is gated. Say so in the
report rather than reporting it as verified. `samples/site/static/staticwebapp.config.json` is the routing contract the
proxies emulate — `/admin/*` and `/api/manage/*` require the `admin` role, `/f/*` rewrites to
the DOI page.

**A keyboard criterion needs a human. Both browsers fail it, for opposite reasons — do not spend a
round rediscovering this.**

*The in-app browser reaches the app but delivers no keys.* Tab moves focus and typing works, but Enter
and Space on a focused control do nothing. Measured twice: against a plain `<button type="button">`
(#22) and against a plain `<a href="#target">` (#11), both injected into the page — the anchor recorded
`keydown` events `[]` and zero clicks, and the hash never changed. It is not the app.

*Chrome delivers keys but cannot reach the app.* `dev/start.mjs` binds every port to `127.0.0.1`, and
Chrome resolves `localhost` to `::1`; `127.0.0.1` is refused as well. A throwaway server bound to
`*:8899` answered `curl` and was still `ERR_CONNECTION_REFUSED` in Chrome, so the browser sits in a
different network context from the dev server — not a port or an address-family problem, and not
fixable by changing the binding.

So: ask the human to press Tab and Enter and attribute it to them in the report, or write that the
criterion is unverified and why. Do not infer it from a working mouse click — that is how a keyboard
criterion gets ticked without ever being exercised. (Done for #22 and again for #11; both times the
human confirmed it works and the run itself could not show it.)

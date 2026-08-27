# Sample site

A minimal bilingual Hugo site that imports the Entriqa module from `hugo/` the same way a
customer site does. It exists so the product can be run, demonstrated and acceptance-tested
from a fresh clone, without anyone having to supply a Hugo site of their own.

## Running it

```
npm install
npm run dev
```

`dev/start.mjs` defaults to this directory, so no `--site` argument is needed. Website on
<http://localhost:4280>, admin on <http://localhost:4281>. See
`.claude/skills/local-dev/SKILL.md` for what comes up on which port and why 1313 and 5100
are the wrong ones to test against.

To work on the site alone, without Azurite and the API (forms will render but not submit):

```
npm run sample:serve
```

## What it demonstrates

| Page | Form | Shows |
|---|---|---|
| Kontakt / Contact | `kontakt` | The basic types: text, email, textarea, consent |
| Beratungsanfrage / Consulting request | `beratung` | The other twelve field types, a page break, a conditional field, a rating scale and a file upload |
| Whitepaper | `whitepaper` | Lead magnet: double opt-in before the CRM step and the download link |
| Selbsttest / Self-check | `selbsttest` | Quiz with a jump to a result, percentage scoring and findings |

Between them the four forms cover every field type and all three form types; the tests in
`api/tests/Entriqa.Tests/SeedFormsTests.cs` fail if that stops being true, or if a sample
would be rejected by the publish check.

The form definitions live in `seed/forms/*.json`, not here — `DevSeedHostedService` publishes
them at startup. It skips forms that already exist, so after editing a definition you have to
delete the form in the admin or wipe `dev/.azurite` before the change shows up.

## Two things worth knowing

**The module redirect must be absolute.** The site imports
`github.com/andrekraemer/entriqa/hugo` and relies on `HUGO_MODULE_REPLACEMENTS` pointing at
this clone. Both `dev/start.mjs` and `scripts/hugo.mjs` set it to an absolute path, because
Hugo does not reliably resolve a relative replacement against the project directory on
Windows — it fails with "module does not exist". This is also why the redirect is not written
into `hugo.toml`.

**The DOI pages only exist in the default language.** `bestaetigen/`, `bestaetigt/` and `f/`
come from the module's own `content/`, and Hugo mounts module content into the default
content language only. The English tree therefore has no confirmation pages. A bilingual
customer site has the same gap, so a real fix belongs in the module, not here.

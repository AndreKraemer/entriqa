# Entriqa — Projekt-Baseline

Selbst gehostetes Formular- und Einsendungssystem für Hugo-Websites auf Azure Static Web
Apps: Formulare, Quizzes, Uploads und eine konfigurierbare Verarbeitungs-Pipeline (E-Mail,
Brevo, Double-Opt-in, PDF, Teams, Webhook), administriert über einen Blazor-WASM-Admin.

Der Prozess (Story → Plan → roter Test → Implementierung → Review → PR) steckt im
pairmode-Plugin. Diese Datei hält nur, was projektspezifisch ist.

## Harte Regeln

- **Sprache:** Code, Kommentare, Testnamen und Commit-/PR-Texte auf **Englisch**.
  Dokumentation, Issues und Nutzertexte auf **Deutsch**. Tests nach Given/When/Then.
- **Keine Live-Endpunkte** aus Tests oder Dev-Loops: Brevo, produktives Azure Storage und
  Teams-Webhooks sind in `pairmode.config.json` gesperrt. Lokal läuft Azurite.
- **Kein Browser-Storage und keine Cookies** beim Website-Besucher (§ 25 TDDDG) — das ist
  ein Produktversprechen, kein Implementierungsdetail. Siehe README.
- `TreatWarningsAsErrors` ist projektweit an; eine Warnung ist ein Fehler.
- **`admin/` ist nicht Teil von `api/Entriqa.sln`**, referenziert aber `Entriqa.Domain`.
  Wer die Domain anfasst, muss den Admin mitbauen — das Gate tut das.

## Aufbau

| Ordner | Inhalt |
|---|---|
| `hugo/` | Hugo-Modul: Shortcode, Embed-Partial, forms.js/css, DOI-Seiten. **Markdown hier ist Produkt, keine Doku.** |
| `api/` | .NET 10, Azure Functions isolated, Clean Architecture (Domain/Application/Data/Infrastructure/Functions) |
| `admin/` | Blazor-WASM-Admin unter `/admin` |
| `dev/` | `node dev/start.mjs` startet Azurite + SWA-CLI lokal |
| `seed/`, `docs/`, `pipelines/`, `tools/` | Beispieldaten, Doku, Kunden-Vorlagen, Lizenzwerkzeug |

## Verify

| Zweck | Kommando |
|---|---|
| Schnelles Gate (jede Änderung) | `node scripts/verify.mjs` |
| Volles Gate (vor dem PR) | `node scripts/verify.mjs --full` |
| Lokale Umgebung starten | `node dev/start.mjs` |

Das schnelle Gate baut und testet `api/` in Debug und baut den Admin. Das volle Gate macht
dasselbe in Release und führt zusätzlich die beiden Publishes aus, die `release.yml` beim
Tag-Build fährt. Exit 0 = PASS.

## Skills

Noch keine Projekt-Skills angelegt (`skills` in `pairmode.config.json` ist leer).
Kandidaten, nach erwartetem Nutzen sortiert:

| Bereich | Warum |
|---|---|
| Verarbeitungs-Pipeline | Brevo, Double-Opt-in, PDF, Teams, Webhook — die dichteste Domänenlogik im Produkt |
| Test-Konventionen | 71 Tests existieren; die Konvention steht nirgends geschrieben |
| Admin/UI-Design | `eq-*`-Klassen, Formular-Builder, Live-Vorschau DE/EN |
| Lokal starten & debuggen | Azurite + SWA-CLI; Voraussetzung für `/pairmode:acceptance` |

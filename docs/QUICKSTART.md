# Entriqa Quickstart – von null zur laufenden Site

Voraussetzungen: eine Hugo-Site (0.140+), Azure-Subscription, Azure DevOps oder GitHub Actions.
Auf deinem Rechner brauchst du **kein .NET** – API und Admin kommen fertig gebaut aus den Releases.

## 1. Azure-Ressourcen (einmalig)

1. **Static Web App** anlegen (Standard-Tier, Region nach Wahl) und die Custom Domain umziehen.
2. **Storage Account** anlegen (Tables + Blob; die Tabellen und der private Container
   `forms-private` entstehen beim ersten Start von selbst).
3. App-Settings der SWA setzen (Konfiguration → Anwendungseinstellungen):

| Setting | Wert |
|---|---|
| `Entriqa__SiteName` | Anzeigename der Site |
| `Entriqa__BaseUrl` | `https://www.deine-domain.tld` |
| `Entriqa__TokenSecret` | ≥ 32 zufällige Zeichen |
| `Entriqa__IpHashSalt` | zufälliger Wert |
| `Entriqa__HousekeepingKey` | zufälliger Wert (für den 15-min-Schedule) |
| `Entriqa__Storage__ConnectionString` | Connection String des Storage Accounts |
| `Entriqa__Brevo__ApiKey` / `__SenderName` / `__SenderEmail` | dein Brevo-Konto |
| `Entriqa__Locales` | z. B. `de,en` (Standard) |
| `Entriqa__LicenseKey` | dein Lizenzschlüssel (Produktivbetrieb) |

4. In der SWA-**Rollenverwaltung** die Admin-Personen mit der Rolle `admin` einladen.

## 2. Hugo-Site verdrahten

1. Hugo-Modul einbinden (`hugo mod init`, falls noch nicht geschehen):

```toml
# hugo.toml
[[module.imports]]
path = "github.com/andrekraemer/entriqa/hugo"
```

2. In `layouts/_default/baseof.html` vor `</body>`:

```
{{ partial "forms-assets.html" . }}
```

3. `staticwebapp.config.json` in die Repo-Wurzel (Vorlage in `docs/`): schützt `/admin/*` und
   `/api/manage/*` mit der Rolle `admin` und rewritet `/f/{slug}/` auf die Standalone-Seite.

4. Formular einbetten – im Content per Shortcode oder im Layout per Partial:

```
{{</* form "kontakt" */>}}
{{ partial "forms/embed" (dict "slug" "kontakt" "page" .) }}
```

## 3. API und Admin deployen (aus den Releases)

Jedes [Release](../../releases) enthält zwei Artefakte:

- **`entriqa-api-vX.Y.Z.zip`** – die kompilierte Functions-API → wird als `api`-Ordner der SWA
  deployt (`swa deploy … --api-location`).
- **`entriqa-admin-vX.Y.Z.zip`** – der gebaute Admin → wird nach `public/admin/` entpackt
  (die `base href` steht bereits auf `/admin/`).

Beispiel-Pipeline (DevOps wie GitHub Actions, sinngemäß):

```yaml
- hugo --minify
- unzip entriqa-admin-vX.Y.Z.zip -d public/admin
- unzip entriqa-api-vX.Y.Z.zip   -d entriqa-api
- swa deploy ./public --api-location ./entriqa-api --deployment-token $(SWA_TOKEN)
```

## 4. Housekeeping-Schedule (Pflicht)

Eine geplante Pipeline ruft alle 15 Minuten `POST /api/housekeeping` mit dem Header
`x-housekeeping-key` auf (Vorlage: `pipelines/azure-pipelines-housekeeping.yml`). Sie zieht
liegengebliebene Hintergrund-Schritte nach und setzt die Aufbewahrungsfristen durch.

## 5. Erstes Formular

`https://deine-domain/admin` öffnen (Anmeldung über die SWA), „Neues Formular" → Vorlage wählen
→ im Builder anpassen → **Veröffentlichen**. Den angezeigten Shortcode in eine Seite einsetzen –
fertig.

## Lokal entwickeln (optional, mit .NET SDK + Functions Core Tools)

```bash
npm install && node dev/start.mjs
# Website:  http://localhost:4280   ·   Admin: http://localhost:4281
```

Ohne Brevo-Key landen alle Mails als klickbare HTML-Dateien in `%TEMP%/entriqa-devmails/` –
damit ist auch der komplette Double-Opt-in-Ablauf lokal durchspielbar.

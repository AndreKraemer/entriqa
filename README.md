# Entriqa

**Alles, was deine Website hört – empfangen, verarbeitet, zugestellt.**

Entriqa ist ein selbst gehostetes Formular- und Einsendungssystem für Hugo-Websites auf Azure
Static Web Apps: Formulare, Selbsttests/Quizzes, Datei-Uploads und eine Verarbeitungs-Pipeline
(E-Mail, Brevo-CRM, Double-Opt-in, PDF, Teams, Webhooks) – administriert über eine
Weboberfläche, ganz ohne Cookie-Banner.

## Warum Entriqa?

- **Kein iframe, kein Fremd-Styling** – Formulare rendern als semantisches HTML mit stabilen
  `eq-*`-Klassen direkt im Theme deiner Seite.
- **Banner-frei by design** – keine Cookies, kein Browser-Storage beim Besucher (§ 25 TDDDG);
  Spam-Schutz über Honeypot, signierte Einweg-Token und Rate-Limits statt Captcha.
- **Formulare, die mehr können** – bedingte Felder, mehrseitige Formulare, Bewertungsskalen,
  Datei-Uploads mit strenger Whitelist, Quizzes mit Weichen und serverseitiger Auswertung.
- **Verarbeitung als Pipeline** – je Formular konfigurierbar: interne Benachrichtigung,
  Brevo-Kontakt und -Firma, eigenes Double-Opt-in (POST-bestätigt, scanner-sicher),
  befristete Download-Links, PDF-Erzeugung, Teams-Karte, Webhook.
- **Admin inklusive** – visueller Formular-Builder mit Live-Vorschau (DE/EN), Einsendungs-
  Posteingang, Kontakt- und Firmen-Historie mit DSGVO-Löschung, Auswertungen mit
  Abschlussquote. Responsive bis aufs Smartphone.
- **Deine Daten bleiben deine** – Azure Table Storage + privater Blob-Container in deiner
  eigenen Subscription; Aufbewahrungsfristen automatisch durchgesetzt.

## Installation

Siehe [docs/QUICKSTART.md](docs/QUICKSTART.md). Kurzfassung: Hugo-Modul einbinden, API- und
Admin-Artefakte aus den [Releases](../../releases) deployen, App-Settings setzen – fertig.

## Lizenz

Der Quellcode ist einsehbar (source-available). **Entwicklung, Test und Staging sind kostenlos;
der Produktivbetrieb erfordert eine Lizenz pro Site** (mehrere Domains mit identischem Inhalt =
eine Site): 149 €/Jahr bei jährlicher Zahlung, andernfalls 199 €/Jahr. Details in
[LICENSE.md](LICENSE.md), Lizenzbezug unter <https://andrekraemer.de/entriqa>. Der
Lizenzschlüssel wird als App-Setting `Entriqa__LicenseKey` eingetragen und offline geprüft –
kein Phone-Home.

## Repository-Aufbau

| Ordner | Inhalt |
|---|---|
| `hugo/` | Hugo-Modul: Shortcode, Embed-Partial, forms.js/css, i18n, DOI-Seiten |
| `src/Core/` | .NET-10-Kern: Domain, Application, Data, Infrastructure (Clean Architecture) |
| `src/Hosts/` | Azure Functions (isolated) und das Lizenz-Schlüsselwerkzeug |
| `src/Ui/` | Blazor-WASM-Admin (läuft unter `/admin`) |
| `tests/` | Unit- und Architekturtests |
| `samples/site/` | Zweisprachige Beispielseite; `npm run dev` startet sie ohne weitere Angabe |
| `seed/` | Beispiel-Formulardefinitionen und Lead-Magnet-Dateien für den lokalen Start |
| `dev/`, `scripts/` | Lokale Umgebung und Verify-Gate |
| `pipelines/` | Azure-DevOps-Vorlagen (u. a. Housekeeping-Schedule) |
| `docs/` | Quickstart und Spezifikation |

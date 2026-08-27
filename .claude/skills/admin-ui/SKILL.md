---
name: admin-ui
description: How the Blazor admin is built — German strings as translation keys in Ui.cs, the visual form builder and its models, the schematic preview, and the eq-* class contract the website relies on. Use for any area:admin or ux issue, and before changing any admin string or the generated markup reference.
---

# The Blazor admin

22 files under `src/Ui/Entriqa.Admin`, served at `/admin`. Pages, Components, Services — no
further ceremony.

It references `Entriqa.Domain`, so a domain change can break it. Since the projects moved into
the standard layout it is part of `Entriqa.slnx`, and the gate builds the whole solution before
running the tests — `dotnet test` on its own only builds what the test project depends on,
which used to let admin breakage through unnoticed.

## The trap: German strings are the translation keys

`Services/Ui.cs` is the interface language. There is no resource file and no key namespace —
**the German string is the key**:

```razor
@inject Ui T
<button>@T["Speichern"]</button>      @* de: "Speichern" · en: "Save" *@
```

`T[…]` returns the key itself for German and looks it up in the `En` dictionary otherwise; a
missing entry falls back to German rather than showing a raw key. `T.F("Frage {0}", n)` formats.

**Changing a German string silently breaks its English translation.** Rename in `Ui.cs` and at
every call site in the same edit, or add the new key alongside. This is also the one place the
project's English-everywhere rule does not apply: these strings are shipped UI text.

The language choice lives in `localStorage` and a switch reloads the app. That is intentional
and does **not** violate the product's no-storage promise — that promise covers the *website
visitor* (§ 25 TDDDG), not an authenticated admin behind SWA auth.

## The form builder

`Pages/FormEditor.razor` edits a definition through `Services/BuilderModel.cs`
(`FormModel`/`FieldModel`/`StepModel`/`LTextModel`), which parses the definition JSON and writes
it back. Two things about that round trip:

- **Multi-language texts are normalised on write.** A text is edited per locale and reduced
  again when saving — if every locale carries the same value it is written back as a plain
  string, not `{de: …, en: …}`. Both forms are valid input (`LText`), so a diff that flips
  between them is normalisation, not a change.
- **The quiz is passed through unchanged** by the builder model and edited in the JSON tab plus
  `Components/QuizEditor.razor`.

`Components/LTextInput.razor` is the editor for any localized text: one input per locale, the
locale prefix hidden when the form has only one language, and non-active locales are *kept*
when the builder's language switch is set.

**Validation is the real thing, live.** `BuilderModel` calls `QuizEngine.Check` from
`Entriqa.Domain` in the browser — the same rules that run when publishing. That is the reason
for the project reference; do not reimplement a check in the admin that the domain already has.

## The preview is schematic, not a rendering

`Components/FormPreview.razor` uses its own `pv-*` classes (`pv-f`, `pv-opt`, `pv-sec`,
`pv-scale`, `pv-cond`, …) and default styling. It shows *structure*, not what the visitor sees —
on the website the theme's CSS applies. Do not try to make it pixel-accurate, and do not put
`eq-*` classes into it. In `Interactive` mode every element reports its selection back to the
builder, which is what makes click-to-edit work.

## The eq-* class contract

`Services/MarkupGenerator.cs` renders the markup a theme developer styles against — a stable
contract per spec section 8/v1, shown in the admin for copy-paste. Structure:

```
eq-form  eq-form--{type}  eq-form--{slug}
  eq-form__intro
  eq-field  eq-field--{type}  eq-field--required
    eq-field__label  eq-field__control  eq-field__help  eq-field__error
    eq-field__options  eq-field__option  eq-field__scale  eq-field__scale-option
  eq-section  eq-divider  eq-page
  eq-quiz__progress  eq-quiz__question  eq-quiz__option  eq-quiz__result  eq-quiz__findings
  eq-actions  eq-submit  eq-message  eq-loading
```

**The source of truth is `hugo/assets/js/forms.js`, not the generator** — the reference has to
follow it, never the other way round. `scripts/check-eq-classes.mjs` enforces that in the verify
gate: it collects every class forms.js puts into the DOM and fails when one is missing from
`MarkupGenerator.Classes`. The two files sit in different projects (forms.js is a Hugo asset,
the generator is in `admin/`, which is outside `Entriqa.slnx`), so no unit test can compare
them — hence a script.

If the guard reports a class, the fix is one of two things: document it, or stop emitting it in
forms.js. Never silence the check. A class that reaches a customer's theme and then disappears
is a breaking change for them.

Modifiers (`--busy`, `--selected`) count as documented when they are named in the prose of their
base class's entry; child elements (`__result-title`) need an entry of their own.

`hugo/assets/css/forms.css` styles only 15 base classes — it deliberately provides structure,
not looks, so the page's own CSS wins. Do not add cosmetic rules there.

## Working on it

The admin runs at `http://localhost:4281` behind the SWA auth emulator; port 5100 bypasses that
gate. See [`local-dev`](../local-dev/SKILL.md). Pipeline steps shown in the step editor come
from the catalogue described in [`processing-pipeline`](../processing-pipeline/SKILL.md), and
`ConfigSchema` is what renders their configuration form — a new step needs no admin change.

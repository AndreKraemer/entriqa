#!/usr/bin/env node
// Guards the visitor-facing texts of the Hugo module (#30): they come from i18n, never from the
// template itself, the confirm request tells the API which language the PAGE is in, and every key
// a layout uses exists in both shipped languages.
//
// Like check-eq-classes.mjs this is a script rather than a unit test: hugo/ has no .NET test host,
// and the failure it guards against is invisible from the outside - a missing key renders an empty
// string, and Hugo says nothing about it unless the build is asked to (--printI18nWarnings, which
// is why check 4 exists at all).
//
// Exit 0 = every visitor text is localizable and translated, 1 = one of the four checks failed.

import { readFileSync, readdirSync, statSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join, relative } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const layoutsDir = join(root, "hugo", "layouts");

const findings = [];
const fail = (file, message) => findings.push(`${relative(root, file)}: ${message}`);

const walk = (dir) => readdirSync(dir).flatMap((name) => {
  const path = join(dir, name);
  return statSync(path).isDirectory() ? walk(path) : [path];
});
const layouts = walk(layoutsDir).filter((p) => p.endsWith(".html")).sort();

// Hugo comments carry German prose on purpose - they explain the template to the next developer
// and never reach a visitor. They come out first, or every check below reads them as UI text.
const withoutComments = (src) => src.replace(/\{\{-?\s*\/\*[\s\S]*?\*\/\s*-?\}\}/g, "");

// Actions are masked BEFORE anything is split on angle brackets: an action may legitimately
// contain < or >, and a scan that removes actions afterwards would already have torn the file
// at the wrong place. The mask is a character that cannot occur in a template.
// Written as an escape, not as the byte itself: a literal NUL would make git treat this
// file as binary - no readable diff in a review, and git grep skips it.
const MASK = "\u0000";
const maskActions = (src) => src.replace(/\{\{[\s\S]*?\}\}/g, MASK);

const hasLetter = (s) => /[A-Za-zÄÖÜäöüß]/.test(s);

// ---------------------------------------------------------------------------------------------
// 1. No fixed-language text in a layout.
//
// Markup is strict: ANY letter in a text node is a finding, because a template that wants to show
// a word has an action for it. The four attributes below are text nodes for a screen reader, so
// they are held to the same standard - role= and aria-live= carry single lowercase tokens and are
// deliberately not in the list.
//
// A script cannot be held to that: 'button', 'application/json' and the class names are letters
// too. Two rules there instead. The message channels - what actually reaches the visitor - may
// never take a literal at all, whatever its length. Everything else falls back to a heuristic: a
// literal reads as UI text when it carries three or more word-ish tokens and is not a plain
// lowercase class list, which lets 'eq-message eq-message--' pass. The heuristic alone would miss
// a short message like 'Bestätigung fehlgeschlagen', which is exactly why the channel rule exists.
const UI_ATTRIBUTES = /\b(?:aria-label|title|placeholder|alt)\s*=\s*"([^"]*)"/g;
const MESSAGE_CHANNELS = /(?:\bsay\(|\.textContent\s*=\s*|\.innerHTML\s*=\s*)\s*('((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)")/g;

// JS comments come out before any literal is read. They are prose for the next developer, and an
// apostrophe in one ("Don't touch this") otherwise reads as the start of a string literal and
// produces a finding quoting half a sentence - the guard failing on its own explanatory text.
//
// Only a // that STARTS its line counts, which is the shape of every comment in these templates.
// Stripping // anywhere took the rest of the line with it, string literals included, so a single
// https:// in a message hid the message from both rules below - the fix for the false positive
// had opened a bigger hole than it closed.
const withoutJsComments = (body) => body.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/^[ \t]*\/\/[^\n]*/gm, " ");

for (const file of layouts) {
  const src = withoutComments(readFileSync(file, "utf8"));
  const masked = maskActions(src);

  const scripts = [...masked.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi)];
  const markup = masked.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, "");

  for (const [, text] of markup.matchAll(/>([^<]*)</g)) {
    if (hasLetter(text)) fail(file, `text in the template instead of i18n: ${JSON.stringify(text.trim())}`);
  }

  for (const [, value] of markup.matchAll(UI_ATTRIBUTES)) {
    if (hasLetter(value)) fail(file, `attribute text instead of i18n: ${JSON.stringify(value.trim())}`);
  }

  for (const [, rawBody] of scripts) {
    const body = withoutJsComments(rawBody);

    for (const m of body.matchAll(MESSAGE_CHANNELS)) {
      const literal = m[2] ?? m[3] ?? "";
      if (hasLetter(literal)) fail(file, `text handed to the visitor instead of i18n: ${JSON.stringify(literal)}`);
    }

    for (const m of body.matchAll(/'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)"/g)) {
      const literal = m[1] ?? m[2] ?? "";
      const words = literal.split(/\s+/).filter(hasLetter);
      const classList = words.every((w) => /^[a-z0-9_-]+$/.test(w));
      if (words.length >= 3 && !classList) {
        fail(file, `text in the script instead of i18n: ${JSON.stringify(literal)}`);
      }
    }
  }
}

// ---------------------------------------------------------------------------------------------
// 2. The confirm request carries the language of the PAGE.
//
// Without it the API answers in the language of the BROWSER (RequestLocale.From falls through to
// Accept-Language), so the one error text the visitor is most likely to see - an expired or
// replayed token - arrives in German on an English page while every string around it is English.
// The sibling templates already hand the page language over as data-lang.
const confirmPath = join(layoutsDir, "_default", "doi-confirm.html");
const confirm = withoutComments(readFileSync(confirmPath, "utf8"));
// \b, or the guard reads a renamed xfetch( as the call it was looking for and its loud-failure
// branch can never be reached - which is how it behaved when the mutation was first run.
const fetchCall = confirm.match(/\bfetch\(([^\n]*)/);
if (!fetchCall) {
  fail(confirmPath, "no fetch call found - this guard no longer knows where the confirm request is built; update it.");
} else if (!/lang=/.test(fetchCall[1]) || !/Language\.Lang/.test(fetchCall[1])) {
  fail(confirmPath, `the confirm request does not pass the page language: ${fetchCall[1].trim()}`);
}

// ---------------------------------------------------------------------------------------------
// 3. Every key a layout uses is translated in both shipped languages.
//
// A key that exists in one file and not the other renders as the EMPTY STRING in the other
// language - no error, no warning, a green build and a button with no label.
const i18nDir = join(root, "hugo", "i18n");
const keysOf = (lang) => {
  // The VALUE, not just the section: a key present with other = "" is worse than a missing one.
  // Hugo then falls back to the default language, so the English page renders the German wording
  // while this guard, the gate and the build all stay green - the exact defect #30 exists to fix.
  const toml = readFileSync(join(i18nDir, `${lang}.toml`), "utf8");
  const translated = new Set();
  let section = null;
  for (const line of toml.split(/\r?\n/)) {
    const header = line.match(/^\[([A-Za-z0-9_]+)\]/);
    if (header) { section = header[1]; continue; }
    const value = line.match(/^\s*other\s*=\s*"(.*)"\s*$/);
    if (value && section && value[1].trim().length > 0) translated.add(section);
  }
  return translated;
};
const translations = { de: keysOf("de"), en: keysOf("en") };

const used = new Map();
for (const file of layouts) {
  for (const m of withoutComments(readFileSync(file, "utf8")).matchAll(/i18n\s+"([A-Za-z0-9_]+)"/g)) {
    if (!used.has(m[1])) used.set(m[1], file);
  }
}
for (const [key, file] of used) {
  for (const lang of ["de", "en"]) {
    if (!translations[lang].has(key)) fail(file, `i18n key "${key}" is missing or empty in hugo/i18n/${lang}.toml`);
  }
}

// ---------------------------------------------------------------------------------------------
// 4. The sample build reports missing translations.
//
// Hugo swallows them by default, so "builds without warnings" is satisfied by a build in which
// half the strings are empty. The flag is what gives that criterion something to say.
const hugoScriptPath = join(root, "scripts", "hugo.mjs");
const hugoScript = readFileSync(hugoScriptPath, "utf8");
const buildArgs = hugoScript.match(/first === "build"\s*\?\s*\[([^\]]*)\]/);
if (!buildArgs) {
  fail(hugoScriptPath, "no build argument list found - this guard no longer knows how the build is assembled; update it.");
} else if (!buildArgs[1].includes("--printI18nWarnings")) {
  fail(hugoScriptPath, `the build does not ask Hugo for missing translations: [${buildArgs[1].trim()}]`);
}

// ---------------------------------------------------------------------------------------------
if (findings.length) {
  console.error("\nHugo module texts are not fully localizable.\n");
  for (const f of findings) console.error(`  ${f}`);
  console.error("\nA visitor-facing text belongs in hugo/i18n/de.toml and en.toml and is read with");
  console.error("{{ i18n \"key\" }} - a text in the template is served in one language to everyone.\n");
  process.exit(1);
}

console.log(`Hugo i18n: ${layouts.length} layouts, ${used.size} keys, translated in de and en.`);

#!/usr/bin/env node
// Guards the eq-* class contract: every class forms.js puts into the DOM has to appear in the
// reference the admin shows theme developers (MarkupGenerator.Classes).
//
// The two live in different projects - forms.js is a Hugo asset, MarkupGenerator sits in
// src/Ui - and the JS side has no .NET test host at all. Hence
// this script, run by scripts/verify.mjs.
//
// Exit 0 = the reference covers everything, 1 = it has drifted.

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const js = readFileSync(join(root, "hugo", "assets", "js", "forms.js"), "utf8");
const generator = readFileSync(join(root, "src", "Ui", "Entriqa.Admin", "Services", "MarkupGenerator.cs"), "utf8");

// Only the Classes array counts as the reference. The sample markup in Render() names classes
// too, but a theme developer reads the annotated list - matching against the whole file would
// let an undocumented class pass just because Render() happens to emit it.
const start = generator.indexOf("Classes =");
if (start < 0) {
  console.error("MarkupGenerator.Classes not found - did the reference move?");
  process.exit(1);
}
const reference = generator.slice(start);

// Only lines that actually assign classes. Without this filter the attribute `data-eq-ready`
// reads as a class named eq-ready and produces a phantom finding. The match ends on an
// alphanumeric so a built-up name like 'eq-message--' + kind contributes its base class
// rather than a dangling modifier.
const emitted = new Set();
for (const line of js.split(/\r?\n/)) {
  if (!/\bclass\b|classList|className/.test(line)) continue;
  for (const m of line.matchAll(/\beq-[a-z0-9_-]*[a-z0-9]/g)) emitted.add(m[0]);
}

// Parse the tuples into base -> meaning rather than substring-matching the file. A plain
// includes() cannot tell eq-quiz__finding from eq-quiz__findings, so removing the former still
// looked documented - and searching a modifier across the WHOLE reference was just as blind:
// dropping --error from the eq-field entry stayed green because eq-message still mentioned it,
// and a fabricated eq-quiz--selected passed because --selected is documented on
// eq-quiz__option. Both were found by mutating the guard and watching it stay green.
const meanings = new Map();
for (const m of reference.matchAll(/\("(eq-[a-z0-9_-]+)",\s*"((?:[^"\\]|\\.)*)"\)/g)) meanings.set(m[1], m[2]);

// A modifier counts as documented when its base class is listed and the modifier is named in
// THAT entry's prose - the reference writes them as "Modifier --selected", not as full classes.
// The trailing boundary keeps --error from being satisfied by --errors or --error_x.
const documented = (cls) => {
  const [base, modifier] = cls.split("--");
  const meaning = meanings.get(base);
  if (meaning === undefined) return false;
  return modifier === undefined || new RegExp(`--${modifier}(?![\\w-])`).test(meaning);
};

const missing = [...emitted].filter((c) => !documented(c)).sort();

if (missing.length) {
  console.error("\neq-* class reference has drifted from forms.js.");
  console.error("Missing from MarkupGenerator.Classes:\n");
  for (const c of missing) console.error(`  ${c}`);
  console.error("\nThe reference is shown to theme developers in the admin - a class they never");
  console.error("see cannot be styled. Add it, or stop emitting it in forms.js.\n");
  process.exit(1);
}

console.log(`eq-* class contract: ${emitted.size} classes, all documented.`);

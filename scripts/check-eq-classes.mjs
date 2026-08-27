#!/usr/bin/env node
// Guards the eq-* class contract: every class forms.js puts into the DOM has to appear in the
// reference the admin shows theme developers (MarkupGenerator.Classes).
//
// The two live in different projects - forms.js is a Hugo asset, MarkupGenerator sits in
// admin/, which is not part of api/Entriqa.sln - so a normal unit test cannot see both. Hence
// this script, run by scripts/verify.mjs.
//
// Exit 0 = the reference covers everything, 1 = it has drifted.

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const js = readFileSync(join(root, "hugo", "assets", "js", "forms.js"), "utf8");
const generator = readFileSync(join(root, "admin", "Entriqa.Admin", "Services", "MarkupGenerator.cs"), "utf8");

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

// Parse the tuples rather than substring-matching the file. A plain includes() cannot tell
// eq-quiz__finding from eq-quiz__findings, so removing the former from the reference still
// looked documented - the guard reported green on real drift.
const listed = new Set();
for (const m of reference.matchAll(/\("(eq-[a-z0-9_-]+)"/g)) listed.add(m[1]);

// A modifier counts as documented when its base class is listed and the modifier is named in
// the prose - the reference writes them as "Modifier --selected", not as a full class. The
// trailing boundary keeps --error from being satisfied by --errors.
const documented = (cls) => {
  const [base, modifier] = cls.split("--");
  if (!listed.has(base)) return false;
  return modifier === undefined || new RegExp(`--${modifier}(?![a-z0-9-])`).test(reference);
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

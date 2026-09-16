#!/usr/bin/env node
// Runs the Hugo binary vendored by hugo-bin against samples/site, with the module import
// redirected to this clone.
//
//   node scripts/hugo.mjs serve        live preview on http://localhost:1313
//   node scripts/hugo.mjs build        one-off build into samples/site/public
//   node scripts/hugo.mjs <args...>    anything else is passed straight to hugo
//
// The redirect has to be an ABSOLUTE path: Hugo does not resolve a relative module
// replacement against the project directory on Windows, it silently looks in the wrong
// place and fails with "module does not exist". dev/start.mjs sets the same variable.

import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const siteRoot = join(repoRoot, "samples", "site");
// The vendored binary itself, not the .bin shim: since Node 20 spawning a .cmd without a
// shell fails, and going through a shell just to start a build is not worth the quoting risk.
const hugo = join(repoRoot, "node_modules", "hugo-bin", "vendor", process.platform === "win32" ? "hugo.exe" : "hugo");

if (!existsSync(hugo)) {
  console.error("hugo not found — run `npm install` first (hugo-bin vendors the binary).");
  process.exit(1);
}

const [first, ...rest] = process.argv.slice(2);
// --printI18nWarnings, or a missing translation is invisible: Hugo renders the key as an empty
// string and says nothing, so a build with half the visitor texts blank still reports success.
const args = first === "build" ? ["--gc", "--minify", "--printI18nWarnings", ...rest]
  : first === "serve" ? ["serve", ...rest]
  : [first, ...rest].filter(Boolean);

const res = spawnSync(hugo, args, {
  cwd: siteRoot,
  stdio: "inherit",
  env: { ...process.env, HUGO_MODULE_REPLACEMENTS: `github.com/andrekraemer/entriqa/hugo -> ${join(repoRoot, "hugo")}` },
});
if (res.error) {
  console.error(`failed to start hugo: ${res.error.message}`);
  process.exit(1);
}
process.exit(res.status ?? 1);

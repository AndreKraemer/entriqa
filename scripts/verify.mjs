#!/usr/bin/env node
// Entriqa verify gate. The only contract: exit 0 = PASS, anything else = FAIL.
//
//   node scripts/verify.mjs          fast gate  — Debug build + tests + admin build
//   node scripts/verify.mjs --full   full gate  — the same in Release, plus the
//                                                 admin publish that release.yml does
//
// The admin no longer needs a build step of its own: it is part of Entriqa.slnx, so a domain
// change that breaks it fails the solution build. Before the projects moved it sat outside
// the solution and had to be built separately or the breakage went unnoticed.

import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const full = process.argv.includes("--full");
const config = full ? "Release" : "Debug";

const steps = [
  // Build the whole solution first: `dotnet test` alone only builds what the test project
  // depends on, so a broken admin or KeyTool would slip through unnoticed - verified by
  // breaking the admin on purpose and watching the gate stay green.
  ["Solution build", "dotnet", ["build", "Entriqa.slnx", "-c", config, "--nologo"]],
  ["Tests", "dotnet", ["test", "Entriqa.slnx", "-c", config, "--nologo", "--no-build"]],
  // forms.js is a Hugo asset with no .NET test host, so the class contract needs a script.
  ["eq-* class contract", process.execPath, [join(root, "scripts", "check-eq-classes.mjs")]],
];

if (full) {
  steps.push(["Admin publish", "dotnet", ["publish", "src/Ui/Entriqa.Admin", "-c", "Release", "--nologo", "-o", "out/verify-admin"]]);
  steps.push(["Functions publish", "dotnet", ["publish", "src/Hosts/Entriqa.Functions", "-c", "Release", "--nologo", "-o", "out/verify-api"]]);
}

let failed = null;
for (const [name, cmd, args] of steps) {
  process.stdout.write(`\n=== ${name} (${config}) ===\n`);
  const res = spawnSync(cmd, args, { cwd: root, stdio: "inherit" });
  if (res.error) { failed = `${name}: ${res.error.message}`; break; }
  if (res.status !== 0) { failed = `${name} exited ${res.status}`; break; }
}

if (failed) {
  process.stdout.write(`\nVERIFY FAILED — ${failed}\n`);
  process.exit(1);
}
process.stdout.write(`\nVERIFY PASSED (${config})\n`);

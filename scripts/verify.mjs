#!/usr/bin/env node
// Entriqa verify gate. The only contract: exit 0 = PASS, anything else = FAIL.
//
//   node scripts/verify.mjs          fast gate  — Debug build + tests + admin build
//   node scripts/verify.mjs --full   full gate  — the same in Release, plus the
//                                                 admin publish that release.yml does
//
// Why the admin is built separately: admin/Entriqa.Admin is NOT part of
// api/Entriqa.sln but references Entriqa.Domain. Without this step a domain change
// can break the admin and `dotnet test api` stays green.

import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const full = process.argv.includes("--full");
const config = full ? "Release" : "Debug";

const steps = [
  ["API tests", "dotnet", ["test", "api/Entriqa.sln", "-c", config, "--nologo"]],
  ["Admin build", "dotnet", ["build", "admin/Entriqa.Admin/Entriqa.Admin.csproj", "-c", config, "--nologo"]],
  // forms.js and the class reference in MarkupGenerator live in different projects, so no unit
  // test can compare them - see the script's header.
  ["eq-* class contract", process.execPath, [join(root, "scripts", "check-eq-classes.mjs")]],
];

if (full) {
  steps.push(["Admin publish", "dotnet", ["publish", "admin/Entriqa.Admin", "-c", "Release", "--nologo", "-o", "out/verify-admin"]]);
  steps.push(["Functions publish", "dotnet", ["publish", "api/src/Entriqa.Functions", "-c", "Release", "--nologo", "-o", "out/verify-api"]]);
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

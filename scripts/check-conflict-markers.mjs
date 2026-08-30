#!/usr/bin/env node
// Fails when a tracked file still carries an unresolved merge conflict.
//
// The compiler catches this in C#, and nowhere else. A leftover marker in Markdown, JSON, a .razor
// template or a seed definition builds, tests and publishes green - so a half-finished resolution
// reaches main looking exactly like a finished one. Three stories collided on the same shared
// skills while #3 was open, so this is a recurring shape, not a one-off.
//
// A file must contain BOTH the opening and the closing marker at the start of a line to count.
// Requiring the pair is what keeps the check quiet: "=======" alone is a Markdown setext heading
// underline, and prose about merge conflicts (this project's skills discuss them) quotes single
// markers without being conflicted.

import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");

/// Tracked, non-binary files whose lines start with this marker. git grep exits 1 for "no match",
/// which is a normal outcome here and not an error.
function filesContaining(pattern) {
  const res = spawnSync("git", ["grep", "-I", "-l", "-E", pattern], { cwd: root, encoding: "utf8" });
  if (res.error) {
    console.error(`check-conflict-markers: could not run git grep - ${res.error.message}`);
    process.exit(2);
  }
  if (res.status > 1) {
    console.error(`check-conflict-markers: git grep exited ${res.status}\n${res.stderr}`);
    process.exit(2);
  }
  return new Set(res.stdout.split("\n").map(l => l.trim()).filter(Boolean));
}

const opened = filesContaining("^<<<<<<< ");
const closed = filesContaining("^>>>>>>> ");
const conflicted = [...opened].filter(f => closed.has(f)).sort();

if (conflicted.length > 0) {
  console.error("Unresolved merge conflict in:");
  for (const f of conflicted) console.error(`  ${f}`);
  console.error("\nFinish the resolution before committing. Resolve against the merge base and keep");
  console.error("what each side changed - taking one side wholesale silently reverts the other story.");
  process.exit(1);
}

console.log(`no conflict markers (${opened.size + closed.size} partial marker(s) in prose ignored)`);

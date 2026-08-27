// Starts the complete local Entriqa dev environment: npm install && node dev/start.mjs
//
//   http://localhost:4280        website (Hugo) with working forms
//   http://localhost:4281        form admin (sign in under /.auth/login/aad with the role "admin" first)
//
// Prozesse: Azurite (Storage-Emulator) · Functions-API · Blazor-Admin · Hugo · 2× SWA-CLI-Proxy.
// Without a Brevo key, mails end up as clickable HTML files in <TEMP>/entriqa-devmails/.
//
// The Hugo site comes from --site <path> or the environment variable ENTRIQA_SITE_ROOT;
// without either, the sample site bundled in samples/site is used, so a fresh clone runs
// with no external site at all. Its Hugo module import of
// github.com/andrekraemer/entriqa/hugo is redirected automatically via HUGO_MODULE_REPLACEMENTS
// to this clone - local changes to layouts and assets take effect right away.
//
// Prerequisites outside npm: .NET SDK (8+), Azure Functions Core Tools v4 and Go
// (for Hugo modules). hugo, azurite and swa are also looked for in the node_modules of the site
// and of this repository.
//   Windows: the Core Tools ship with Visual Studio (they are found automatically).
//   macOS:   brew install --cask dotnet-sdk
//            brew tap azure/functions && brew install azure-functions-core-tools@4
import { spawn, spawnSync } from "node:child_process";
import { existsSync, readdirSync, mkdirSync } from "node:fs";
import { join, dirname, resolve, delimiter } from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..");

const siteArg = (() => {
  const i = process.argv.indexOf("--site");
  return i >= 0 ? process.argv[i + 1] : undefined;
})();
const bundledSite = join(repoRoot, "samples", "site");
const siteRoot = resolve(siteArg ?? process.env.ENTRIQA_SITE_ROOT ?? bundledSite);
const looksLikeSite = ["hugo.toml", "hugo.yaml", "config.toml", "config"].some(f => existsSync(join(siteRoot, f)));
if (!looksLikeSite) {
  console.error(`\nKeine Hugo-Site gefunden in: ${siteRoot}\n  Site angeben mit: node dev/start.mjs --site <pfad>  (oder ENTRIQA_SITE_ROOT setzen)\n  Ohne Angabe wird die Beispielseite aus samples/site verwendet.\n`);
  process.exit(1);
}

// make hugo, azurite and swa findable from the node_modules of the site and of this repository
const binPaths = [join(siteRoot, "node_modules", ".bin"), join(repoRoot, "node_modules", ".bin")]
  .filter(existsSync);
const env = { ...process.env };
// Windows calls the variable "Path" - extend the existing key instead of adding a second one
const pathKey = Object.keys(env).find(k => k.toUpperCase() === "PATH") ?? "PATH";
env[pathKey] = [...binPaths, env[pathKey]].filter(Boolean).join(delimiter);
// redirect the module import of the site to this clone (unless set explicitly otherwise)
env.HUGO_MODULE_REPLACEMENTS ??= `github.com/andrekraemer/entriqa/hugo -> ${join(repoRoot, "hugo")}`;

function findFunc() {
  // Azure Functions Core Tools: PATH or (on Windows) the Visual Studio installation
  if (process.platform === "win32" && process.env.LOCALAPPDATA) {
    const vs = join(process.env.LOCALAPPDATA, "AzureFunctionsTools", "Releases");
    if (existsSync(vs)) {
      const versions = readdirSync(vs).filter(v => /^\d/.test(v)).sort((a, b) => b.localeCompare(a, undefined, { numeric: true }));
      for (const v of versions) {
        const exe = join(vs, v, "cli_x64", "func.exe");
        if (existsSync(exe)) return exe;
      }
    }
  }
  return "func";
}

function preflight(cmd, name, hint, args = ["--version"]) {
  const probe = spawnSync(cmd, args, { shell: true, stdio: "ignore", env });
  if (probe.status !== 0) {
    console.error(`\nFehlt: ${name} („${cmd} --version" schlug fehl).\n  ${hint}\n`);
    process.exit(1);
  }
}

const funcCmd = findFunc();
preflight("dotnet", ".NET SDK", "Windows: mit Visual Studio · macOS: brew install --cask dotnet-sdk");
preflight(funcCmd === "func" ? "func" : `"${funcCmd}"`, "Azure Functions Core Tools v4",
  "Windows: mit Visual Studio · macOS: brew tap azure/functions && brew install azure-functions-core-tools@4");
preflight("hugo", "Hugo (extended)", "z. B. hugo-bin in den devDependencies der Site oder brew install hugo", ["version"]);
preflight("go", "Go (für Hugo Modules)", "winget install GoLang.Go · brew install go", ["version"]);

const colors = { azurite: 90, api: 36, admin: 35, hugo: 32, site: 34, adminui: 33 };
function run(name, cmd, args, cwd) {
  const child = spawn(cmd, args, { cwd, shell: true, env });
  const prefix = `\x1b[${colors[name] ?? 37}m[${name}]\x1b[0m `;
  const pipe = stream => stream.on("data", d => d.toString().split(/\r?\n/).filter(Boolean).forEach(l => console.log(prefix + l)));
  pipe(child.stdout); pipe(child.stderr);
  child.on("exit", code => console.log(prefix + `beendet (Code ${code})`));
  return child;
}

async function waitFor(url, label, tries = 60) {
  for (let i = 0; i < tries; i++) {
    try { await fetch(url); return; } catch { await new Promise(r => setTimeout(r, 1000)); }
  }
  console.log(`Warnung: ${label} antwortet nicht – weiter trotzdem.`);
}

const azuriteDir = join(repoRoot, "dev", ".azurite");
mkdirSync(azuriteDir, { recursive: true });

console.log(`Entriqa-Dev-Umgebung startet …\n  Produkt: ${repoRoot}\n  Site:    ${siteRoot}\n`);
run("azurite", "azurite", ["--silent", "--location", `"${azuriteDir}"`]);
await waitFor("http://127.0.0.1:10002/devstoreaccount1", "Azurite");   // otherwise the dev seed of the API fails
run("api", funcCmd === "func" ? "func" : `"${funcCmd}"`, ["start", "--port", "7071"], join(repoRoot, "src", "Hosts", "Entriqa.Functions"));
run("admin", "dotnet", ["run", "--urls", "http://localhost:5100"], join(repoRoot, "src", "Ui", "Entriqa.Admin"));
// baseURL = proxy origin, otherwise absolute URLs (icon fonts, mask SVGs) point at :1313 and fail CORS.
run("hugo", "hugo", ["serve", "--port", "1313", "--baseURL", "http://localhost:4280/", "--appendPort=false"], siteRoot);

// The SWA proxies wait by themselves until app and API are reachable.
setTimeout(() => {
  // --swa-config-location: the emulator only applies routes and role rules when it is told
  // where staticwebapp.config.json lives. Without it /api/manage/* is reachable unauthenticated
  // and the /f/* rewrite is missing, so local behaviour silently differs from production.
  run("site", "swa", ["start", "http://localhost:1313", "--api-devserver-url", "http://localhost:7071",
    "--swa-config-location", `"${join(siteRoot, "static")}"`, "--port", "4280"], siteRoot);
  run("adminui", "swa", ["start", "http://localhost:5100", "--api-devserver-url", "http://localhost:7071", "--port", "4281"], siteRoot);
  setTimeout(() => {
    console.log("\n──────────────────────────────────────────────────");
    console.log("  Website:  http://localhost:4280");
    console.log("  Admin:    http://localhost:4281  (Login: /.auth/login/aad, Rolle \"admin\" eintragen)");
    console.log(`  Dev-Mails: ${join(tmpdir(), "entriqa-devmails")}`);
    console.log("──────────────────────────────────────────────────\n");
  }, 4000);
}, 5000);

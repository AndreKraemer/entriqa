// Startet die komplette lokale Formularsystem-Umgebung: npm run formsystem
//
//   http://localhost:4280        Website (Hugo) mit funktionierenden Formularen
//   http://localhost:4281        Formular-Admin (erst unter /.auth/login/aad mit Rolle "admin" einloggen)
//
// Prozesse: Azurite (Storage-Emulator) · Functions-API · Blazor-Admin · Hugo · 2× SWA-CLI-Proxy.
// Ohne Brevo-Key landen Mails als klickbare HTML-Dateien in <TEMP>/entriqa-devmails/.
//
// Voraussetzungen außerhalb von npm: .NET-SDK (8+) und Azure Functions Core Tools v4.
//   Windows: Core Tools kommen mit Visual Studio mit (werden automatisch gefunden).
//   macOS:   brew install --cask dotnet-sdk
//            brew tap azure/functions && brew install azure-functions-core-tools@4
import { spawn, spawnSync } from "node:child_process";
import { existsSync, readdirSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const formsystem = join(root, "formsystem");

function findFunc() {
  // Azure Functions Core Tools: PATH oder (Windows) die Visual-Studio-Installation
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

function preflight(cmd, name, hint) {
  const probe = spawnSync(cmd, ["--version"], { shell: true, stdio: "ignore" });
  if (probe.status !== 0) {
    console.error(`\nFehlt: ${name} („${cmd} --version" schlug fehl).\n  ${hint}\n`);
    process.exit(1);
  }
}

const funcCmd = findFunc();
preflight("dotnet", ".NET SDK", "Windows: mit Visual Studio · macOS: brew install --cask dotnet-sdk");
preflight(funcCmd === "func" ? "func" : `"${funcCmd}"`, "Azure Functions Core Tools v4",
  "Windows: mit Visual Studio · macOS: brew tap azure/functions && brew install azure-functions-core-tools@4");

const colors = { azurite: 90, api: 36, admin: 35, hugo: 32, site: 34, adminui: 33 };
function run(name, cmd, args, cwd) {
  const child = spawn(cmd, args, { cwd, shell: true, env: process.env });
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

const azuriteDir = join(formsystem, ".azurite");
mkdirSync(azuriteDir, { recursive: true });

console.log("Formularsystem-Dev-Umgebung startet …\n");
run("azurite", "azurite", ["--silent", "--location", `"${azuriteDir}"`]);
await waitFor("http://127.0.0.1:10002/devstoreaccount1", "Azurite");   // sonst scheitert der Dev-Seed der API
run("api", funcCmd === "func" ? "func" : `"${funcCmd}"`, ["start", "--port", "7071"], join(formsystem, "api", "src", "Entriqa.Functions"));
run("admin", "dotnet", ["run", "--urls", "http://localhost:5100"], join(formsystem, "admin", "Entriqa.Admin"));
// baseURL = Proxy-Origin, sonst zeigen absolute URLs (Icon-Fonts, Masken-SVGs) auf :1313 und scheitern am CORS.
run("hugo", "hugo", ["serve", "--port", "1313", "--baseURL", "http://localhost:4280/", "--appendPort=false"], root);

// Die SWA-Proxys warten selbst, bis App und API erreichbar sind.
setTimeout(() => {
  run("site", "swa", ["start", "http://localhost:1313", "--api-devserver-url", "http://localhost:7071", "--port", "4280"], root);
  run("adminui", "swa", ["start", "http://localhost:5100", "--api-devserver-url", "http://localhost:7071", "--port", "4281"], root);
  setTimeout(() => {
    console.log("\n──────────────────────────────────────────────────");
    console.log("  Website:  http://localhost:4280");
    console.log("  Admin:    http://localhost:4281  (Login: /.auth/login/aad, Rolle \"admin\" eintragen)");
    console.log(`  Dev-Mails: ${join(tmpdir(), "entriqa-devmails")}`);
    console.log("──────────────────────────────────────────────────\n");
  }, 4000);
}, 5000);

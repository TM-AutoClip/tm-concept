// TM Concept: renders the Revideo project a model wrote (C#: TMConcept.Engines RevideoEngine). Experimental.
//
//   node render.mjs --work "<work folder>" --out "<silent.mp4>" --width 1080 --height 1920 [--workers 1] --chrome "<chrome-headless-shell>"
//
// Work folder (written by C#): src/project.ts + src/input.ts (the app's files), src/scene.tsx (the model's file, already checked in C#),
// input.json. Packages resolve from THIS folder's node_modules through a Vite alias; nothing is copied into the work folder.
// The browser has no network (a proxy that does not exist; the local Vite server is localhost and bypasses it) and no GPU.
// Telemetry is off (DISABLE_TELEMETRY=true, set by C#). Temporary files go to the work folder (TEMP).
// stdout: one JSON object per line: {"event":"progress"|"done"|"error", ...}. Exit code 0 done, 1 error.
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync } from "node:fs";
import net from "node:net";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const ROOT = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const write = (o) => process.stdout.write(JSON.stringify(o) + "\n");

const args = (() => {
  const a = {};
  const v = process.argv.slice(2);
  for (let i = 0; i < v.length; i++) {
    if (!v[i].startsWith("--")) continue;
    a[v[i].slice(2)] = i + 1 < v.length && !v[i + 1].startsWith("--") ? v[++i] : "true";
  }
  return a;
})();
const number = (name, fallback) => {
  const n = Number(args[name]);
  return Number.isFinite(n) && n > 0 ? Math.floor(n) : fallback;
};

const freePort = () =>
  new Promise((resolve, reject) => {
    const s = net.createServer();
    s.once("error", reject);
    s.listen(0, "127.0.0.1", () => {
      const p = s.address().port;
      s.close(() => resolve(p));
    });
  });

const main = async () => {
  const work = args.work ? path.resolve(args.work) : "";
  const out = args.out ? path.resolve(args.out) : "";
  const chrome = args.chrome ?? "";
  if (!work || !out) throw new Error("--work and --out are required.");
  if (!existsSync(path.join(work, "src", "project.ts"))) throw new Error("src/project.ts is missing.");
  if (!chrome || !existsSync(chrome)) throw new Error("Chrome Headless Shell was not found.");
  const input = JSON.parse(readFileSync(path.join(work, "input.json"), "utf8").replace(/^﻿/, ""));
  const { renderVideo } = require("@revideo/renderer");

  process.chdir(work);
  const temporary = path.join(work, "tmp", "revideo-out");
  rmSync(temporary, { recursive: true, force: true });
  mkdirSync(temporary, { recursive: true });
  const modules = path.join(ROOT, "node_modules").split(path.sep).join("/");

  // Revideo adds "--single-process" itself; on Windows Chrome Headless Shell cannot open the page in that mode. The array claims the flag
  // is already there so it is not added.
  const browserArgs = ["--proxy-server=http://127.0.0.1:9", "--disable-gpu"];
  browserArgs.includes = (x) => x === "--single-process" || Array.prototype.includes.call(browserArgs, x);

  const result = await renderVideo({
    projectFile: "./src/project.ts",
    variables: { input },
    settings: {
      outFile: "video.mp4",
      outDir: temporary,
      workers: number("workers", 1),
      logProgress: false,
      projectSettings: {
        size: { x: number("width", 1080), y: number("height", 1920) },
        exporter: { name: "@revideo/core/ffmpeg", options: { format: "mp4" } },
      },
      ffmpeg: { ffmpegLogLevel: "error" },
      puppeteer: { headless: "shell", executablePath: chrome, args: browserArgs },
      viteBasePort: await freePort(),
      viteConfig: {
        root: work,
        cacheDir: path.join(work, "tmp", "vite"),
        logLevel: "error",
        resolve: { alias: [{ find: /^@revideo\/(.*)$/, replacement: modules + "/@revideo/$1" }] },
        server: { fs: { strict: true, allow: [work, ROOT] } },
      },
      progressCallback: (_worker, ratio) => write({ event: "progress", ratio, message: `${Math.round(ratio * 100)} %` }),
    },
  });
  rmSync(out, { force: true });
  renameSync(result, out);
  rmSync(temporary, { recursive: true, force: true });
  write({ event: "done" });
};

main()
  .then(() => process.exit(0))
  .catch((e) => {
    write({ event: "error", message: String(e?.message ?? e).split("\n").slice(0, 6).join(" | ") });
    process.exit(1);
  });

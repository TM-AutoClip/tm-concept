// TM Concept: renders the Remotion project a model wrote (C#: TMConcept.Engines RemotionEngine).
//
//   node render.mjs --work "<work folder>" --out "<silent.mp4>" --browser "<chrome-headless-shell>" [--crf 20] [--jpeg 90] [--concurrency 2]
//                   [--gl swangle]
//   node render.mjs --ensure-browser
//
// --browser is required: given no browser, Remotion would download one during the render (it is installed with --ensure-browser).
// Work folder (written by C#): src/index.ts, src/Root.tsx, src/input.ts (the app's files), src/Scene.tsx (the model's file, already
// checked in C#), input.json (props), public/ (images). Packages (remotion, react) resolve from THIS folder's node_modules; nothing is
// copied into the work folder and no model code is written here. Every resolved module must be inside the work folder or this
// node_modules (webpack resolve.restrictions), so a model file cannot pull a file from elsewhere into the bundle.
// stdout: one JSON object per line: {"event":"bundle"|"progress"|"done"|"error", ...}. Exit code 0 done, 1 error.
import { existsSync, readFileSync, renameSync, rmSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { bundle } from "@remotion/bundler";
import { ensureBrowser, renderMedia, selectComposition } from "@remotion/renderer";

const ROOT = path.dirname(fileURLToPath(import.meta.url));
process.chdir(ROOT);

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

// Inside one of the given folders (case-insensitive on Windows; both separators).
const insideOf = (...roots) => {
  const sep = "[\\\\/]";
  const escape = (s) => s.split(/[\\/]+/).filter((p) => p.length > 0).map((p) => p.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join(sep);
  const start = (r) => (path.resolve(r).startsWith("/") ? sep : "") + escape(path.resolve(r)) + sep;
  return new RegExp("^(?:" + roots.map(start).join("|") + ")", process.platform === "win32" ? "i" : "");
};

const main = async () => {
  if (args["ensure-browser"]) {
    await ensureBrowser({ logLevel: "error" });
    write({ event: "done" });
    return;
  }

  const work = args.work ? path.resolve(args.work) : "";
  const out = args.out ? path.resolve(args.out) : "";
  const browserExecutable = args.browser && args.browser !== "true" ? path.resolve(args.browser) : "";
  if (!work || !out) throw new Error("--work and --out are required.");
  if (!browserExecutable || !existsSync(browserExecutable)) throw new Error("--browser must name the installed Chrome Headless Shell.");
  const entry = path.join(work, "src", "index.ts");
  if (!existsSync(entry)) throw new Error("src/index.ts is missing.");
  const inputProps = JSON.parse(readFileSync(path.join(work, "input.json"), "utf8").replace(/^﻿/, ""));
  const modules = path.join(ROOT, "node_modules");

  let lastBundle = -1;
  const serveUrl = await bundle({
    entryPoint: entry,
    publicDir: path.join(work, "public"),
    outDir: path.join(work, "bundle"),
    rootDir: work,
    webpackOverride: (c) => ({
      ...c,
      resolve: { ...c.resolve, modules: [modules, "node_modules"], restrictions: [insideOf(work, modules)] },
      resolveLoader: { ...(c.resolveLoader ?? {}), modules: [modules, "node_modules"] },
    }),
    onProgress: (p) => {
      const percent = Math.floor(p);
      if (percent !== lastBundle) {
        lastBundle = percent;
        write({ event: "bundle", percent });
      }
    },
  });

  const composition = await selectComposition({ serveUrl, id: "Concept", inputProps, logLevel: "error", browserExecutable });
  const partial = out.replace(/\.mp4$/i, "") + ".partial.mp4";
  rmSync(partial, { force: true });
  let last = -1;
  await renderMedia({
    composition,
    serveUrl,
    codec: "h264",
    outputLocation: partial,
    inputProps,
    muted: true,
    concurrency: number("concurrency", 2),
    crf: number("crf", 20),
    imageFormat: "jpeg",
    jpegQuality: number("jpeg", 90),
    overwrite: true,
    logLevel: "error",
    browserExecutable,
    chromiumOptions: { gl: args.gl ?? "swangle" },
    onProgress: (p) => {
      const percent = Math.floor(p.progress * 100);
      if (percent === last) return;
      last = percent;
      write({ event: "progress", ratio: p.progress, message: `${p.renderedFrames}/${composition.durationInFrames} frames` });
    },
  });
  rmSync(out, { force: true });
  renameSync(partial, out);
  write({ event: "done" });
};

main()
  .then(() => process.exit(0))
  .catch((e) => {
    write({ event: "error", message: String(e?.message ?? e).split("\n").slice(0, 6).join(" | ") });
    process.exit(1);
  });

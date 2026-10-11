import assert from "node:assert/strict";
import { mkdirSync, readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";
import { buildSync } from "esbuild";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { maxImageBytes, validateImageFile } from "../src/zevImage.ts";
import { mutationKeys, resourceKey } from "../src/dataSync.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

// The component is TSX, so it is compiled with esbuild (shipped with Vite) and rendered to HTML with React's server
// renderer; react stays external so the bundle and the test share one React.
const web = fileURLToPath(new URL("..", import.meta.url));
const outDir = fileURLToPath(new URL("../node_modules/.cache/zev-tests/", import.meta.url));
mkdirSync(outDir, { recursive: true });
buildSync({
  stdin: { contents: `export * from "./src/admin/ZevCover.tsx"; export { default as ZevCover } from "./src/admin/ZevCover.tsx";
    export { primeZevImage } from "./src/zevImage.ts"; export { default as ZevCardGrid } from "./src/admin/ZevCards.tsx";`, resolveDir: web, loader: "ts" },
  bundle: true, format: "esm", platform: "neutral", jsx: "automatic", outfile: `${outDir}zevCover.mjs`,
  external: ["react", "react-dom", "react/jsx-runtime"], logLevel: "silent",
});
const { ZevCover, ZevCoverView, ImageUploadField, emptyImageDraft, primeZevImage, ZevCardGrid } =
  await import(pathToFileURL(`${outDir}zevCover.mjs`).href);
const render = (component, props) => renderToStaticMarkup(createElement(component, props));

test("card cover shows the photo (16:9 box, lazy image with alt text)", () => {
  const html = render(ZevCoverView, { src: "blob:zgrada-1", name: "Zgrada Centar" });
  assert.match(html, /^<div class="zev-cover">/);
  assert.match(html, /<img src="blob:zgrada-1" alt="Fotografija zgrade: Zgrada Centar" loading="lazy"\/>/);
  assert.doesNotMatch(html, /placeholder/);
});

test("without a photo the card shows the building icon on a neutral placeholder", () => {
  const html = render(ZevCoverView, { src: null, name: "Zgrada Centar" });
  assert.match(html, /class="zev-cover placeholder"/);
  assert.match(html, /role="img" aria-label="Zgrada bez fotografije"/);
  assert.match(html, /<svg class="nav-icon"[^>]*><path d="M5 21V4h10v17/, "building icon from the palette");
  assert.doesNotMatch(html, /<img/);
  assert.match(render(ZevCover, { token: "t", imageUrl: null, name: "Bez slike" }), /placeholder/);
});

test("a loaded photo is shown on the first render (session cache per versioned URL)", () => {
  primeZevImage("/api/zevs/z1/image?v=abc", "blob:cached-photo");
  const html = render(ZevCover, { token: "t", imageUrl: "/api/zevs/z1/image?v=abc", name: "Zgrada" });
  assert.match(html, /<img src="blob:cached-photo"/);
  assert.match(render(ZevCover, { token: "t", imageUrl: "/api/zevs/z1/image?v=new", name: "Zgrada" }), /placeholder/,
    "a new version is not served from the old cache entry");
});

test("upload field: picker for JPG/PNG/WEBP, remove button only when there is a photo", () => {
  const empty = render(ImageUploadField, { label: "Fotografija zgrade", token: "t", currentUrl: null, name: "ZEV",
    draft: emptyImageDraft, onChange: () => {} });
  assert.match(empty, /accept="\.jpg,\.jpeg,\.png,\.webp"/);
  assert.match(empty, /Dodaj sliku/);
  assert.doesNotMatch(empty, /Ukloni sliku/);
  primeZevImage("/api/zevs/z2/image?v=1", "blob:saved");
  const saved = render(ImageUploadField, { label: "Fotografija zgrade", token: "t", currentUrl: "/api/zevs/z2/image?v=1",
    name: "ZEV", draft: emptyImageDraft, onChange: () => {} });
  assert.match(saved, /<img src="blob:saved"/);
  assert.match(saved, /Zamijeni sliku/);
  assert.match(saved, /Ukloni sliku/);
  const removing = render(ImageUploadField, { label: "Fotografija zgrade", token: "t", currentUrl: "/api/zevs/z2/image?v=1",
    name: "ZEV", draft: { file: null, remove: true }, onChange: () => {} });
  assert.match(removing, /placeholder/);
  assert.match(removing, /Slika će biti uklonjena pri snimanju/);
});

test("file rules equal the API: JPG/JPEG/PNG/WEBP up to 5 MB, extension must match the type", () => {
  assert.equal(validateImageFile({ name: "zgrada.JPG", type: "image/jpeg", size: 1000 }), null);
  assert.equal(validateImageFile({ name: "zgrada.webp", type: "image/webp", size: maxImageBytes }), null);
  assert.match(validateImageFile({ name: "zgrada.gif", type: "image/gif", size: 1000 }), /JPG, PNG ili WEBP/);
  assert.match(validateImageFile({ name: "zgrada.png", type: "image/jpeg", size: 1000 }), /JPG, PNG ili WEBP/);
  assert.match(validateImageFile({ name: "zgrada.png", type: "image/png", size: maxImageBytes + 1 }), /5 MB/);
  assert.match(validateImageFile({ name: "zgrada.png", type: "image/png", size: 0 }), /prazna/);
  const api = source("../../src/Zev.Api/Endpoints/ZevImageEndpoints.cs");
  assert.match(api, /MaxBytes = 5_000_000/);
  assert.equal(maxImageBytes, 5_000_000);
});

test("home cards: the whole card is one button, the active ZEV is outlined and marked Aktivan", () => {
  primeZevImage("/api/zevs/a/image?v=1", "blob:a");
  const zevs = [{ id: "a", code: "ZEV-A", name: "TEST ZEV A", addressLine: "Titova 1", imageUrl: "/api/zevs/a/image?v=1" },
    { id: "b", code: "ZEV-B", name: "TEST ZEV B", addressLine: null, imageUrl: null }];
  const html = render(ZevCardGrid, { token: "t", zevs, activeId: "b", onSelect: () => {}, detail: (zev) => `Uloga za ${zev.code}` });
  const cards = html.split('<div role="listitem">').slice(1);
  assert.equal(cards.length, 2);
  assert.ok(cards[0].startsWith('<button type="button" class="zev-card selectable" aria-pressed="false" '
    + 'aria-label="TEST ZEV A, otvori radni prostor"><div class="zev-cover"><img src="blob:a"'), cards[0]);
  assert.doesNotMatch(cards[0], /Aktivan/);
  assert.ok(cards[1].startsWith('<button type="button" class="zev-card selectable active" aria-pressed="true" '
    + 'aria-label="TEST ZEV B (aktivni ZEV)">'), cards[1]);
  assert.match(cards[1], /zev-cover placeholder/);
  assert.ok(cards[1].includes('<span class="zev-active-badge">Aktivan</span>'));
  assert.match(cards[1], /Uloga za ZEV-B/);
  assert.equal((html.match(/<button/g) ?? []).length, 2, "no inner buttons: the card itself is the button");
});

test("home screen keeps the community cards above the modules of the active ZEV, in both workspaces", () => {
  for (const file of ["../src/admin/Workspace.tsx", "../src/admin/RoleWorkspace.tsx"]) {
    const page = source(file);
    const overview = page.slice(page.indexOf('{active === "overview" && <>'));
    const grid = overview.indexOf("<ZevCardGrid");
    assert.ok(grid >= 0 && grid < overview.indexOf("<ModuleDashboard"), file);
    assert.ok(overview.includes("activeId={activeZev?.id ?? null} onSelect={selectZev}"), file);
    assert.doesNotMatch(page, /workspaceOpen|Sve zajednice/, "no hidden state that removes the cards");
  }
});

test("ZEV form uses the cover; a photo change refreshes the ZEV lists", () => {
  assert.ok(source("../src/admin/Workspace.tsx").includes('image: { label: "Fotografija zgrade", path: (id) => `/api/zevs/${id}/image`'));
  assert.equal(resourceKey("/api/zevs/z1/image"), "zevs");
  assert.ok(mutationKeys("/api/zevs/z1/image").includes("zevs"));
  const css = source("../src/admin.css");
  assert.ok(css.includes(".zev-cover{aspect-ratio:16/9"));
  assert.match(css, /\.zev-cover img\{[^}]*object-fit:cover/);
  assert.ok(css.includes(".zev-card.selectable.active{border:2px solid var(--zev-primary)"));
  assert.ok(css.includes(".zev-card.selectable .zev-cover{align-self:stretch}"), "placeholder keeps the full 16:9 width");
});

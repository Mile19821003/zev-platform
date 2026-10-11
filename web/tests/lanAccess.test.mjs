import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import test from "node:test";
import { apiBase } from "../src/apiBase.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

test("the app calls /api on its own origin, so it works from a phone via the LAN IP", () => {
  assert.equal(apiBase, "");
  for (const dir of ["../src/", "../src/admin/"]) for (const file of readdirSync(new URL(dir, import.meta.url))) {
    if (!/\.tsx?$/.test(file)) continue;
    const text = source(`${dir}${file}`);
    assert.ok(!text.includes("localhost:8080"), `${dir}${file} still targets localhost:8080`);
    if (file !== "apiBase.ts") assert.ok(!text.includes("VITE_API_URL"), `${dir}${file} reads VITE_API_URL itself`);
  }
});

test("Vite dev server, nginx and the local compose stack forward /api to the API", () => {
  const vite = source("../vite.config.ts");
  assert.ok(vite.includes('"/api": { target: process.env.VITE_PROXY_TARGET ?? "http://localhost:8080"'));
  assert.ok(vite.includes('host: "0.0.0.0"'));
  const nginx = source("../nginx.conf");
  assert.ok(nginx.includes("location /api/ {") && nginx.includes("set $api_upstream http://api:8080;"));
  assert.ok(nginx.includes("client_max_body_size 30m;"));
  assert.ok(source("../../compose.yaml").includes("VITE_API_URL: ${VITE_API_URL:-}"));
  assert.ok(source("../Dockerfile").includes('ARG VITE_API_URL=""'));
  const program = source("../../src/Zev.Api/Program.cs");
  assert.ok(program.includes("if (builder.Environment.IsDevelopment()) policy.SetIsOriginAllowed(_ => true);"));
});

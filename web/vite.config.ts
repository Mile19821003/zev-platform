import { defineConfig } from "vite";

// The app calls /api on its own origin (src/apiBase.ts); in development Vite forwards those requests to the API,
// so a phone opening http://<computer-ip>:5173 reaches the API through this computer instead of its own localhost.
// VITE_PROXY_TARGET points the proxy at another API address.
export default defineConfig({
  server: {
    host: "0.0.0.0",
    proxy: {
      "/api": { target: process.env.VITE_PROXY_TARGET ?? "http://localhost:8080", changeOrigin: false, xfwd: true },
      "/health": { target: process.env.VITE_PROXY_TARGET ?? "http://localhost:8080" },
    },
  },
});

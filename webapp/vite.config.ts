import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Dev server proxies /api + /health to the DataHub API (http profile, :7200)
// so the SPA carries the session origin and CORS stays out of the loop.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": { target: "http://localhost:7200", changeOrigin: true },
      "/health": { target: "http://localhost:7200", changeOrigin: true },
    },
  },
  build: {
    outDir: "dist",
    sourcemap: false,
  },
});

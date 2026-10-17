import { defineConfig, type Plugin } from "vite";
import vue from "@vitejs/plugin-vue";
import tailwindcss from "@tailwindcss/vite";
import mkcert from "vite-plugin-mkcert";
import { fileURLToPath } from "node:url";
import { colorModeScript } from "@wow-two-beta/fe/foundation/primitives";
import { ThemeStorageKey } from "./src/bootstrap/theme";

/** Holds the build's version: CI passes `APP_VERSION`; a local build is `dev`. */
const version = process.env.APP_VERSION || "dev";

/**
 * Serves and emits `/theme.js`: the SDK's pre-paint colour script for the console's storage key, so a dark console
 * never flashes light. A same-origin file rather than an inline script, so a strict Content-Security-Policy keeps it
 * working without a hash.
 */
function themeScript(): Plugin {
  const source = colorModeScript({ storageKey: ThemeStorageKey });
  return {
    name: "wheelhouse-theme-script",
    configureServer(server) {
      server.middlewares.use("/theme.js", (_request, response) => {
        response.setHeader("Content-Type", "text/javascript");
        response.end(source);
      });
    },
    generateBundle() {
      this.emitFile({ type: "asset", fileName: "theme.js", source });
    },
  };
}

// The SPA is served from the .NET host's wwwroot in production (base '/', same-origin "/api/...").
// In dev, Vite runs over HTTPS (vite-plugin-mkcert → a locally-trusted cert, so the Secure auth
// cookie is kept and the OAuth redirect has no cert interstitial) and proxies "/api" to the backend's
// HTTPS profile. changeOrigin:false keeps Host=localhost:5174 so the OAuth redirect_uri + the session
// cookie stay on the dev origin (5174). secure:false accepts the .NET dev cert.
// `--mode http` or VITE_HTTPS=false serves plain HTTP for headless previews; GitHub sign-in needs the HTTPS origin.
export default defineConfig(({ command, mode }) => {
  const https = mode !== "http" && process.env.VITE_HTTPS !== "false";

  return {
    base: "/",
    define: { __APP_VERSION__: JSON.stringify(version) },
    plugins: [vue(), tailwindcss(), themeScript(), ...(command === "serve" && https ? [mkcert()] : [])],
    resolve: {
      alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
    },
    build: {
      outDir: "dist",
      emptyOutDir: true,
    },
    server: {
      port: Number(process.env.VITE_PORT ?? 5174),
      strictPort: true,
      proxy: {
        "/api": {
          target: process.env.WHEELHOUSE_API_PROXY ?? "https://localhost:8210",
          changeOrigin: false,
          secure: false,
        },
      },
    },
  };
});

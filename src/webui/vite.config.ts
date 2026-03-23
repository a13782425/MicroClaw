/// <reference types="vitest/config" />
import { fileURLToPath, URL } from "node:url";
import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import { resolve } from "node:path";
import Components from "unplugin-vue-components/vite";
import AutoImport from "unplugin-auto-import/vite";
import { ElementPlusResolver } from "unplugin-vue-components/resolvers";
// import { compression } from "vite-plugin-compression2";

export default defineConfig(({ mode }) => {
  const microclawEnv = loadEnv(mode, resolve(__dirname, "../../.microclaw"), "");
  const gatewayHost = microclawEnv.GATEWAY_HOST || "localhost";
  const gatewayPort = microclawEnv.GATEWAY_PORT || "5080";
  const gatewayUrl = `http://${gatewayHost}:${gatewayPort}`;

  return {
    plugins: [
      vue(),
      AutoImport({ resolvers: [ElementPlusResolver()] }),
      Components({ resolvers: [ElementPlusResolver()] }),
      // compression({ algorithms: ['brotliCompress'], include: /\.(js|css)$/ }),
    ],
    resolve: {
      alias: {
        "@": fileURLToPath(new URL("./src", import.meta.url))
      }
    },
    build: {
      chunkSizeWarningLimit: 1000,
      rollupOptions: {
        output: {
          manualChunks: {
            'vendor-vue':     ['vue', 'vue-router', 'pinia'],
            'vendor-element': ['element-plus'],
            'vendor-icons':   ['@element-plus/icons-vue'],
            'vendor-mermaid': ['mermaid'],
            'vendor-md':      ['marked', 'dompurify'],
            'vendor-hljs':    ['highlight.js'],
          },
          chunkFileNames:  'assets/[name]-[hash].js',
          entryFileNames:  'assets/[name]-[hash].js',
          assetFileNames:  'assets/[name]-[hash][extname]',
        }
      }
    },
    server: {
      port: 5173,
      proxy: {
        "/api": {
          target: gatewayUrl,
          changeOrigin: true
        },
        "/ws": {
          target: gatewayUrl,
          ws: true,
          changeOrigin: true
        }
      }
    },
    test: {
      environment: 'happy-dom',
      include: ['src/**/*.{test,spec}.{ts,tsx}'],
      css: false,
      server: {
        deps: {
          inline: ['element-plus'],
        },
      },
    },
  };
});
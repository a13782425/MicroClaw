import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tsconfigPaths from 'vite-tsconfig-paths';
import { resolve } from 'node:path';
export default defineConfig(({ mode }) => {
    const microclawEnv = loadEnv(mode, resolve(__dirname, '../../.microclaw'), '');
    const gatewayHost = microclawEnv.GATEWAY_HOST || 'localhost';
    const gatewayPort = microclawEnv.GATEWAY_PORT || '5080';
    const gatewayUrl = `http://${gatewayHost}:${gatewayPort}`;
    function manualChunks(id) {
        const normalizedId = id.replace(/\\/g, '/');
        if (!normalizedId.includes('/node_modules/')) {
            return undefined;
        }
        if (normalizedId.includes('/recharts/')
            || normalizedId.includes('/@chakra-ui/charts/')) {
            return 'vendor-charts';
        }
        if (normalizedId.includes('/marked/')
            || normalizedId.includes('/highlight.js/')
            || normalizedId.includes('/dompurify/')) {
            return 'vendor-markdown';
        }
        if (normalizedId.includes('/mermaid/dist/mermaid.core.mjs')) {
            return 'vendor-mermaid';
        }
        return undefined;
    }
    return {
        plugins: [react(), tsconfigPaths()],
        test: {
            globals: true,
            environment: 'jsdom',
            setupFiles: ['./src/test/setup.ts'],
            include: ['src/**/*.{test,spec}.{ts,tsx}'],
            pool: 'forks',
            maxForks: 1,
            minForks: 1,
            coverage: {
                provider: 'v8',
                reporter: ['text', 'lcov'],
                include: ['src/**/*.{ts,tsx}'],
                exclude: ['src/test/**', 'src/main.tsx', 'src/App.tsx'],
            },
        },
        build: {
            chunkSizeWarningLimit: 1000,
            rollupOptions: {
                output: {
                    manualChunks,
                    chunkFileNames: 'assets/[name]-[hash].js',
                    entryFileNames: 'assets/[name]-[hash].js',
                    assetFileNames: 'assets/[name]-[hash][extname]',
                },
            },
        },
        server: {
            port: 5174,
            proxy: {
                '/api': {
                    target: gatewayUrl,
                    changeOrigin: true,
                },
                '/ws': {
                    target: gatewayUrl,
                    ws: true,
                    changeOrigin: true,
                },
            },
        },
    };
});

import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

export default defineConfig(({ command }) => ({
  plugins: [svelte(), tailwindcss()],
  base: command === 'build' ? './' : '/',
  server: {
    port: 5173,
    strictPort: true,
    host: '127.0.0.1',
  },
  build: {
    outDir: path.resolve(__dirname, '../Firestarter.App/wwwroot'),
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
  },
}))

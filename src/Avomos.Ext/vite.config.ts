import { defineConfig } from 'vite'
import { crx } from '@crxjs/vite-plugin'
import preact from '@preact/preset-vite'
import rawManifest from './manifest.json' with { type: 'json' }
import pkg from './package.json' with { type: 'json' }

export default defineConfig({
  plugins: [preact(), crx({ manifest: { ...rawManifest, version: pkg.version } })],
  build: {
    rollupOptions: {
      input: {
        content: 'src/content.tsx',
      },
    },
  },
})

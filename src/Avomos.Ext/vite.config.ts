import { defineConfig } from 'vite'
import { crx } from '@crxjs/vite-plugin'
import preact from '@preact/preset-vite'
import chromeManifest from './manifest.json' with { type: 'json' }
import firefoxManifest from './manifest.firefox.json' with { type: 'json' }
import pkg from './package.json' with { type: 'json' }

const isFirefox = !!process.env.FIREFOX
const manifest = isFirefox ? firefoxManifest : chromeManifest
const outDir = isFirefox ? 'dist-firefox' : 'dist'

export default defineConfig({
  plugins: [preact(), crx({ manifest: { ...manifest, version: pkg.version } })],
  build: {
    outDir,
    rollupOptions: {
      input: {
        content: 'src/content.tsx',
      },
    },
  },
})

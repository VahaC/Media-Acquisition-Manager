import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config.ts'

// Built on the app's own Vite config so tests run the same React Compiler output, `@` alias and
// `__APP_VERSION__` as the browser does.
export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      globals: true,
      css: false,
      setupFiles: ['./src/test/setup.ts'],
    },
  }),
)

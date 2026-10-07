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
      // Room for the 4 s findBy/waitFor timeout set in setup.ts, which the 5 s default leaves too little of.
      testTimeout: 10_000,
      // One zone on every machine, and not UTC: a date shown in UTC instead of local time then
      // fails a test instead of passing by coincidence on a UTC build agent.
      env: { TZ: 'Europe/Kyiv' },
    },
  }),
)

import type { defaultNS, resources } from './lib/i18n'

// Types every translation key against the English resources, so `t('status.titel')` fails
// `tsc -b` (and the build) instead of rendering the key name on screen.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: typeof defaultNS
    resources: (typeof resources)['en']
    // Without it a call that passes a default value skips the key check: `t('status.titel',
    // 'Status')` would compile and ship the fallback text in every language.
    strictKeyChecks: true
  }
}

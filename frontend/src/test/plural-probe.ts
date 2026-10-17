import { createInstance, type i18n as I18n } from 'i18next'
import { resources } from '@/lib/i18n'

type Namespace = keyof typeof resources.en

/** The plural forms Ukrainian picks between (CLDR): 1, 21…; 2–4, 22–24…; 0, 5–20, 25–30…; fractions. */
const UKRAINIAN_FORMS = ['one', 'few', 'many', 'other'] as const

/**
 * MAM-46 — an i18n instance of its own, in Ukrainian, whose only Ukrainian strings are the four plural
 * forms of `namespace:key`, each `text(form)` (say `few: {{value}}`), so a test sees which form the
 * code's count picked; every other key falls back to English. Hand it to the component under test
 * through `<I18nextProvider i18n={…}>`; the app's own instance, which every other test shares, is
 * left alone.
 */
export function pluralFormProbe(namespace: Namespace, key: string, text: (form: string) => string): I18n {
  const path = key.split('.')
  const last = path.pop()!
  const forms = Object.fromEntries(UKRAINIAN_FORMS.map((form) => [`${last}_${form}`, text(form)]))
  const bundle = path.reduceRight<Record<string, unknown>>((inner, segment) => ({ [segment]: inner }), forms)
  const probe = createInstance()
  void probe.init({
    lng: 'uk',
    fallbackLng: 'en',
    resources: { en: resources.en, uk: { [namespace]: bundle } },
    ns: Object.keys(resources.en),
    defaultNS: 'common',
    interpolation: { escapeValue: false },
    initAsync: false,
  })
  return probe
}

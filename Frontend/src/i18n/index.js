// Minimal i18n: customer-facing strings live in per-locale dictionaries so Arabic (RTL) can be
// added later by adding `ar.js`, switching `locale`, and setting <html dir="rtl">. Layout CSS
// uses logical properties (margin-inline-start, inset-inline-end, …) so it mirrors automatically.
import en from './en.js'

const dictionaries = { en }
const locale = 'en'

export const currentLocale = locale
export const direction = locale === 'ar' ? 'rtl' : 'ltr'

/**
 * Translate a key, interpolating {name} placeholders.
 * @param {string} key e.g. "client.album.download"
 * @param {Record<string, string|number>} [vars]
 */
export function t(key, vars) {
  // Singular forms live under "<key>.one" (Arabic would add .two/.few/.many via Intl.PluralRules).
  const pluralKey = vars?.count !== undefined ? `${key}.${new Intl.PluralRules(locale).select(Number(vars.count))}` : null
  const dict = dictionaries[locale]
  const template = (pluralKey && dict[pluralKey]) ?? dict[key] ?? dictionaries.en[key] ?? key
  if (!vars) return template
  return template.replace(/\{(\w+)\}/g, (_, name) => (vars[name] ?? `{${name}}`).toString())
}

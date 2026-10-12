import { useState } from 'react'
import { Field } from '../ui/Field.jsx'
import { api, ApiError } from '../../services/api.js'
import { t } from '../../i18n/index.js'
import { inquiryOptions } from '../../content/site.js'
import { CITY_SUGGESTIONS, COUNTRIES, DEFAULT_COUNTRY, POPULAR_COUNTRIES, countryName } from '../../content/countries.js'

const empty = {
  name: '', eventType: '', eventOther: '', eventDate: '', country: DEFAULT_COUNTRY, city: '', venueName: '',
  mobile: '', email: '', coverage: '', source: '', message: '', website: '',
}
const OTHER = 'Other'
// The inquiry stores the event type as up to 64 characters: "Other: " + the visitor's description.
const OTHER_MAX = 64 - `${OTHER}: `.length

// Fields the studio needs to reply, per variant.
const requiredBy = {
  packages: ['name', 'venueName', 'mobile', 'coverage'],
  contact: ['name', 'mobile', 'message'],
}

/**
 * The website's two forms; both post to the same endpoint and land in Admin → Requests, under a tab each.
 * - "packages" — a booking request: Full Name · Event Type · Event Date · Country / City · Venue · Mobile ·
 *   Estimated Coverage Time · How did you hear about us? · Message.
 * - "contact" — a short question or message: Name · Mobile · Email (optional) · Message.
 */
export function InquiryForm({ variant = 'packages' }) {
  const isContact = variant === 'contact'
  const required = requiredBy[variant]
  const [values, setValues] = useState(empty)
  const [status, setStatus] = useState('idle') // idle | sending | sent
  const [error, setError] = useState(null)

  const set = (name) => (e) => setValues((v) => ({ ...v, [name]: e.target.value }))
  const fieldError = (name) => error?.fieldErrors?.[name]

  async function submit(e) {
    e.preventDefault()
    const otherChosen = !isContact && values.eventType === OTHER
    const missing = [...required, ...(otherChosen ? ['eventOther'] : [])].filter((k) => !values[k].trim())
    if (missing.length) {
      setError(new ApiError(400, { title: t('common.error'), errors: Object.fromEntries(missing.map((k) => [k, [t('inquiry.required')]])) }))
      e.currentTarget.querySelector(`[name="${missing[0]}"]`)?.focus()
      return
    }
    setStatus('sending')
    setError(null)
    try {
      const { eventOther, country, city, venueName, ...rest } = values
      await api('/api/public/inquiries', {
        method: 'POST',
        // form: "packages" or "contact" — the admin shows them under Package requests / Contact messages.
        body: isContact
          ? { form: variant, name: values.name, mobile: values.mobile, email: values.email.trim() || null, message: values.message, website: values.website }
          : {
              ...rest,
              form: variant,
              // Saved on the inquiry as one line: "Royal Maxim, Cairo, Egypt" (venue is required).
              venue: [venueName.trim(), city.trim(), city.trim() || venueName.trim() ? countryName(country) : ''].filter(Boolean).join(', ').slice(0, 200) || null,
              eventType: otherChosen ? `${OTHER}: ${eventOther.trim()}` : values.eventType,
              eventDate: values.eventDate || null,
              email: null,
            },
      })
      setStatus('sent')
      setValues(empty)
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, { title: t('common.error') }))
      setStatus('idle')
    }
  }

  if (status === 'sent') {
    return (
      <div className="inquiry-sent" role="status">
        <p className="inquiry-sent__title">Thank you!</p>
        <p>{t('inquiry.thanks')}</p>
      </div>
    )
  }

  const hasFieldErrors = error && Object.keys(error.fieldErrors).length > 0

  return (
    <form className="inquiry" onSubmit={submit} noValidate>
      {error && !hasFieldErrors && (
        <p className="form-error" role="alert">
          {error.message}
        </p>
      )}
      <Field
        label={t(isContact ? 'inquiry.nameShort' : 'inquiry.name')}
        name="name"
        autoComplete="name"
        required
        value={values.name}
        onChange={set('name')}
        error={fieldError('name')}
      />
      {!isContact && (
        <Field label={t('inquiry.eventType')} as="select" name="eventType" value={values.eventType} onChange={set('eventType')} error={fieldError('eventType')}>
          <option value="">{t('inquiry.select')}</option>
          {inquiryOptions.eventTypes.map((o) => (
            <option key={o}>{o}</option>
          ))}
        </Field>
      )}
      {!isContact && values.eventType === OTHER && (
        <Field
          label={t('inquiry.eventOther')}
          name="eventOther"
          required
          autoFocus
          maxLength={OTHER_MAX}
          placeholder={t('inquiry.eventOtherPlaceholder')}
          value={values.eventOther}
          onChange={set('eventOther')}
          error={fieldError('eventOther')}
        />
      )}
      {!isContact && (
        <>
          <Field
            label={t('inquiry.eventDate')}
            name="eventDate"
            type="date"
            value={values.eventDate}
            onChange={set('eventDate')}
            error={fieldError('eventDate')}
          />
          {/* Where: any country; city suggestions for the countries the studio shoots in most. */}
          <div className="inquiry__pair">
            <Field label={t('inquiry.country')} as="select" name="country" value={values.country} onChange={set('country')}>
              <optgroup label="Popular">
                {POPULAR_COUNTRIES.map((code) => (
                  <option key={code} value={code}>
                    {countryName(code)}
                  </option>
                ))}
              </optgroup>
              <optgroup label="All countries">
                {COUNTRIES.map((c) => (
                  <option key={c.code} value={c.code}>
                    {c.name}
                  </option>
                ))}
              </optgroup>
            </Field>
            <Field
              label={t('inquiry.city')}
              name="city"
                        autoComplete="address-level2"
              list={CITY_SUGGESTIONS[values.country] ? `cities-${variant}` : undefined}
              maxLength={80}
              value={values.city}
              onChange={set('city')}
              error={fieldError('city')}
            />
          </div>
          {CITY_SUGGESTIONS[values.country] && (
            <datalist id={`cities-${variant}`}>
              {CITY_SUGGESTIONS[values.country].map((c) => (
                <option key={c} value={c} />
              ))}
            </datalist>
          )}
          <Field
            label={t('inquiry.venueName')}
            name="venueName"
            required
            maxLength={100}
            placeholder={t('inquiry.venueNamePlaceholder')}
            value={values.venueName}
            onChange={set('venueName')}
          />
        </>
      )}
      <Field
        label={t('inquiry.mobile')}
        name="mobile"
        type="tel"
        autoComplete="tel"
        inputMode="tel"
        placeholder={t('inquiry.mobilePlaceholder')}
        required
        value={values.mobile}
        onChange={set('mobile')}
        error={fieldError('mobile')}
      />
      {isContact && (
        <Field label={t('inquiry.email')} name="email" type="email" autoComplete="email" maxLength={256} value={values.email} onChange={set('email')} error={fieldError('email')} />
      )}
      {!isContact && (
        <>
          <Field label={t('inquiry.coverage')} as="select" name="coverage" required value={values.coverage} onChange={set('coverage')} error={fieldError('coverage')}>
            <option value="" disabled>
              {t('inquiry.select')}
            </option>
            {inquiryOptions.coverage.map((o) => (
              <option key={o}>{o}</option>
            ))}
          </Field>
          <Field label={t('inquiry.source')} as="select" name="source" value={values.source} onChange={set('source')}>
            <option value="">{t('inquiry.select')}</option>
            {inquiryOptions.sources.map((o) => (
              <option key={o}>{o}</option>
            ))}
          </Field>
        </>
      )}
      <Field
        label={t('inquiry.message')}
        as="textarea"
        name="message"
        rows={isContact ? 5 : 4}
        required={isContact}
        maxLength={4000}
        placeholder={isContact ? t('inquiry.contactMessagePlaceholder') : undefined}
        value={values.message}
        onChange={set('message')}
        error={fieldError('message')}
      />

      {/* Honeypot: invisible to people, irresistible to bots. */}
      <div className="visually-hidden" aria-hidden="true">
        <label>
          Website
          <input tabIndex={-1} autoComplete="off" name="website" value={values.website} onChange={set('website')} />
        </label>
      </div>

      <button type="submit" className="inquiry__submit" disabled={status === 'sending'}>
        {status === 'sending' ? t('inquiry.sending') : t('inquiry.submit')}
      </button>
    </form>
  )
}

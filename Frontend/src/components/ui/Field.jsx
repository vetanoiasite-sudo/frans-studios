import { useId } from 'react'

/**
 * Label + control + error message, wired up for screen readers.
 * Renders an <input> by default; pass `as="textarea"` or `as="select"` (with children for options).
 */
export function Field({ label, error, hint, as: Control = 'input', className = '', children, ...props }) {
  const id = useId()
  const describedBy = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined
  const message = Array.isArray(error) ? error[0] : error
  return (
    <div className={`field ${message ? 'field--invalid' : ''} ${className}`}>
      <label htmlFor={id} className="field__label">
        {label}
      </label>
      <Control id={id} className="field__control" aria-invalid={Boolean(message)} aria-describedby={describedBy} {...props}>
        {children}
      </Control>
      {hint && (
        <p id={`${id}-hint`} className="field__hint">
          {hint}
        </p>
      )}
      {message && (
        <p id={`${id}-error`} className="field__error">
          {message}
        </p>
      )}
    </div>
  )
}

export function Checkbox({ label, hint, ...props }) {
  const id = useId()
  return (
    <div className="checkbox">
      <input id={id} type="checkbox" {...props} />
      <label htmlFor={id}>
        {label}
        {hint && <span className="checkbox__hint">{hint}</span>}
      </label>
    </div>
  )
}

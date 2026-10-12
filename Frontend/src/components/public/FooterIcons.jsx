// Small line/solid icons for the footer (email, phone, social networks). 18px, currentColor.

const common = { width: 18, height: 18, viewBox: '0 0 24 24', 'aria-hidden': true }

export function MailIcon() {
  return (
    <svg {...common} fill="none" stroke="currentColor" strokeWidth="1.5">
      <rect x="3" y="5.5" width="18" height="13" rx="1.5" />
      <path d="m3.5 6.5 8.5 6.5 8.5-6.5" strokeLinejoin="round" />
    </svg>
  )
}

export function PhoneIcon() {
  return (
    <svg {...common} fill="none" stroke="currentColor" strokeWidth="1.5">
      <path
        d="M6.6 3.5h2.6l1.4 4-2 1.3a11 11 0 0 0 6.6 6.6l1.3-2 4 1.4v2.6a2 2 0 0 1-2.1 2A16.5 16.5 0 0 1 4.6 5.6a2 2 0 0 1 2-2.1z"
        strokeLinejoin="round"
      />
      <path d="M15 4.5a5 5 0 0 1 4.5 4.5M15 7.5a2 2 0 0 1 1.5 1.5" strokeLinecap="round" />
    </svg>
  )
}

export function WhatsAppIcon() {
  return (
    <svg {...common} fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M4.5 19.5l1.1-3.6A8 8 0 1 1 8.4 18.6z" strokeLinejoin="round" />
      <path
        d="M9.2 8.6c.2-.4.5-.5.8-.5h.4c.2 0 .4.1.5.4l.6 1.4c.1.2 0 .5-.1.6l-.5.6c.6 1.2 1.5 2.1 2.7 2.7l.6-.5c.2-.2.4-.2.6-.1l1.4.6c.3.1.4.3.4.5v.4c0 .3-.1.6-.5.8-.6.4-1.5.5-2.4.1a8.6 8.6 0 0 1-4.2-4.2c-.4-.9-.3-1.8.1-2.4z"
        fill="currentColor"
        stroke="none"
      />
    </svg>
  )
}

export function ContactIcon({ name }) {
  if (name === 'mail') return <MailIcon />
  if (name === 'phone') return <PhoneIcon />
  if (name === 'whatsapp') return <WhatsAppIcon />
  return null
}

const social = {
  Facebook: (
    <svg {...common} fill="currentColor">
      <path d="M13.5 21v-7.5h2.6l.4-3h-3V8.6c0-.9.3-1.5 1.5-1.5h1.6V4.4a21 21 0 0 0-2.3-.1c-2.3 0-3.8 1.4-3.8 3.9v2.3H7.9v3h2.6V21z" />
    </svg>
  ),
  Pinterest: (
    <svg {...common} fill="currentColor">
      <path d="M12 2.5a9.5 9.5 0 0 0-3.5 18.3c-.1-.8-.2-2 0-2.8l1.2-5.1s-.3-.6-.3-1.5c0-1.4.8-2.5 1.9-2.5.9 0 1.3.7 1.3 1.5 0 .9-.6 2.2-.9 3.4-.2 1 .5 1.9 1.6 1.9 1.9 0 3.3-2 3.3-4.9 0-2.6-1.8-4.3-4.5-4.3a4.6 4.6 0 0 0-4.8 4.6c0 .9.3 1.9.8 2.4l.1.3-.3 1.2c0 .2-.2.3-.4.2-1.3-.6-2.1-2.5-2.1-4.1 0-3.3 2.4-6.4 7-6.4 3.7 0 6.6 2.6 6.6 6.2 0 3.7-2.3 6.6-5.5 6.6-1.1 0-2.1-.6-2.5-1.2l-.7 2.6c-.2 1-.9 2.2-1.4 2.9A9.5 9.5 0 1 0 12 2.5z" />
    </svg>
  ),
  TikTok: (
    <svg {...common} fill="currentColor">
      <path d="M16.6 3h-3.1v12.3a2.7 2.7 0 1 1-2.7-2.7c.3 0 .5 0 .8.1V9.5a5.9 5.9 0 1 0 5 5.8V9.1a7.6 7.6 0 0 0 4.4 1.4V7.4A4.4 4.4 0 0 1 16.6 3z" />
    </svg>
  ),
  Instagram: (
    <svg {...common} fill="none" stroke="currentColor" strokeWidth="1.6">
      <rect x="3" y="3" width="18" height="18" rx="5" />
      <circle cx="12" cy="12" r="4.2" />
      <circle cx="17.3" cy="6.7" r="0.9" fill="currentColor" stroke="none" />
    </svg>
  ),
}

export function SocialIcon({ name }) {
  return social[name] ?? null
}

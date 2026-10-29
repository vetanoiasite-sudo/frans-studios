// Studio copy and contact details, migrated from the current fransstudios.com.
// Portfolio galleries, films and packages are managed in the admin dashboard instead.

export const studio = {
  name: 'Vetanoia',
  tagline: 'Love. Art. Captured.',
  intro:
    'Celebrate your unique love story by capturing every detail with a touch of sophistication and timeless beauty.',
  footerLine: 'Capturing moments that words can’t.',
  footerAbout:
    'We’re a team of professional wedding photographers and cinematographers driven by a love for storytelling and a passion for capturing emotions, ready to preserve your most cherished moments.',
  email: 'info@vetanoia.com',
  phoneDisplay: '+20 122 171 1458',
  phoneFooter: '+201221711458',
  phoneHref: 'tel:+201221711458',
  whatsappHref: 'https://wa.me/201221711458',
  instagramHref: 'https://www.instagram.com/vetanoia_solutions',
  instagramHandle: '@vetanoia_solutions',
}

// Footer social icons (design order). An icon only appears once its link is filled in.
export const socials = [
  { name: 'Facebook', href: null },
  { name: 'Instagram', href: studio.instagramHref },
]

// Footer contact lines (latest design).
export const footerContacts = [
  { icon: 'mail', label: studio.email, href: `mailto:${studio.email}` },
  { icon: 'phone', label: `Egypt ${studio.phoneFooter}`, href: studio.phoneHref },
  { icon: 'whatsapp', label: `Egypt ${studio.phoneFooter}`, href: studio.whatsappHref, external: true },
]

// Bottom bar credit.
export const copyrightHolder = 'VETANOIA'

// Home page stats band (values and labels from the Figma design). Update here when the numbers change.
export const stats = [
  { value: '1M +', label: 'Stunning Shots' },
  { value: '1200 +', label: 'Happy Clients' },
  { value: '60K +', label: 'Followers' },
  { value: '10 +', label: 'Years Of Experience' },
]

export const founder = {
  name: 'Joe Frans',
  role: 'Founder & Lead Photographer',
  heading: 'Hi_I’m Joe Frans',
  signature: 'Frans _ Founder & Lead Photographer',
  lead: 'I believe great photography begins with loving people and feeling deeply.',
  paragraphs: [
    'My work sits between documentary and artistic storytelling — always guided by emotion, never forced. I’m drawn to the quiet, in-between moments: a glance, a touch, a feeling that might go unnoticed, yet becomes unforgettable.',
    'For me, people and their stories will always matter more than places. A beautiful setting means little without real connection and genuine emotion.',
    'Inspired by traveling and experiencing weddings across different cultures, I approach every celebration with fresh eyes and deep curiosity. My style is natural, honest, and timeless, with a strong love for film tones and black & white imagery where emotion speaks the loudest.',
    'Beyond creating photographs, I bring calm energy into your day — present, supportive, and never in the way — allowing moments to unfold naturally.',
    'Because in the end, the most meaningful photographs are simply real moments, honestly felt.',
  ],
}

// About page "Meet Our Family" (copy from the live fransstudios.com/about). Order follows the design grid.
// Photos live in src/assets/team/<photo>.jpg (384 × 600).
export const family = {
  title: 'Meet Our Family',
  intro:
    'We’re a tight-knit group at Vetanoia, and we love bringing your ideas to life! With every project, we work together to create something special, all while having fun and making sure you feel right at home.',
  members: [
    { name: 'Mina Fathy', photo: 'mina-fathy' },
    { name: 'Fady Khela', photo: 'fady-khela' },
    { name: 'Lora Nagy', photo: 'lora-nagy' },
    { name: 'Mina Zarif', photo: 'mina-zarif' },
    { name: 'Monica Adel', photo: 'monica-adel' },
    { name: 'Merna Sobhy', photo: 'merna-sobhy' },
    { name: 'Gina Safwat', photo: 'gina-safwat' },
  ],
  testimonial: {
    quote: 'Joe presence added a value to our wedding, every one loved him, the pictures are insane. he is considered one of the family now',
    name: 'Andrew Mansour',
  },
}

// About page testimonial (Figma: quote, script signature, small "NAME · BRIDE" label).
export const aboutTestimonial = {
  quote:
    'I had a specific reference which was the vintage film look for the cinematography, and the results exceeds my expectations. Everyone was impressed by how friendly and fun was Joe and his team',
  name: 'Mariam Sameh',
  role: 'Bride',
}

// FAQ from the live fransstudios.com/contact-us page. Each answer is a list of paragraphs.
export const faq = [
  {
    q: 'What is your photography style?',
    a: [
      'Our style isn’t one-size-fits-all—it adapts to suit each person’s character and vibe. Whether it’s timeless, trendy, or somewhere in between, we make sure the look of each session reflects who you truly are.',
      'We believe in capturing the beauty of real moments and emotions through an artistic lens. Our style is candid and more focused on aesthetics, creating photos that feel both natural and visually stunning.',
    ],
  },
  {
    q: 'How many photographers do you work with?',
    a: ['All Packages include two Professional Photographers.'],
  },
  {
    q: 'How do we reserve you for the wedding?',
    // The live answer ends at "non-refundable" — "deposit" completes it; confirm with the studio.
    a: ['To ensure our services for your Wedding we require a 50% non-refundable deposit.'],
  },
]

export const inquiryOptions = {
  // Options from the live fransstudios.com/packages-2 form.
  // Event type: edit freely — whatever is picked is saved on the inquiry as text. Keep 'Other' last: picking it
  // asks the visitor to describe the event, saved as "Other: <description>".
  eventTypes: ['Birthday', 'Engagement', 'Wedding', 'Other'],
  coverage: ['Half day', 'Full day', 'Not sure yet'],
  sources: ['Instagram', 'Friend', 'Wedding Planner', 'Website'],
}

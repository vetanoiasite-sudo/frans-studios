// Built-in films shown until the studio publishes its own in Admin → Films, so a fresh checkout (or a
// database without uploaded media) still has working players. Files live in public/films: the video at
// /films/<slug>.mp4 and its poster at /films/<slug>/<width>.(jpg|webp), the layout ResponsiveImage reads.

const film = (slug, title, description) => ({
  id: `builtin-${slug}`,
  title,
  description,
  poster: { src: `/films/${slug}`, width: 1920, height: 1080, widths: [480, 960, 1600, 1920], alt: title },
  videoUrl: `/films/${slug}.mp4`,
  videoMimeType: 'video/mp4',
})

export const defaultFilms = [
  film('sophia-daniel', 'Sophia & Daniel — Just Married', 'Confetti, cheers and their first steps out of the church as husband and wife.'),
  film('michael-linda', 'Michael & Linda — Highlights', 'A seaside wedding in Alexandria, from the first look to the last dance.'),
  film('william-chloe', 'William & Chloe — Teaser', 'Sun, sea and an intimate ceremony in El Gouna.'),
  film('james-katrina', 'James & Katrina — The Proposal', 'The surprise proposal, as it happened.'),
]

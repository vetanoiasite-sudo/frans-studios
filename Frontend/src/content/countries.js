/**
 * Countries for the inquiry form: ISO 3166-1 alpha-2 codes, named by the browser (Intl.DisplayNames) in the
 * site's language, so no list of names has to be maintained. "Popular" are listed first (where the studio
 * usually shoots); edit freely.
 */
const CODES =
  'AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ ' +
  'CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO ' +
  'FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE ' +
  'JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO ' +
  'MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW ' +
  'PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM ' +
  'TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS XK YE YT ZA ZM ZW'

export const POPULAR_COUNTRIES = ['EG', 'AE', 'SA', 'KW', 'QA', 'LB', 'JO', 'GR', 'IT', 'AT', 'TR', 'GB', 'US']

export const DEFAULT_COUNTRY = 'EG'

const names = (() => {
  try {
    return new Intl.DisplayNames(['en'], { type: 'region' })
  } catch {
    return null
  }
})()

export const countryName = (code) => names?.of(code) ?? code

/** All countries sorted by name, as { code, name }. */
export const COUNTRIES = CODES.split(' ')
  .map((code) => ({ code, name: countryName(code) }))
  .sort((a, b) => a.name.localeCompare(b.name, 'en'))

/** City suggestions while typing (a hint only: any city can be typed). */
export const CITY_SUGGESTIONS = {
  EG: ['Cairo', 'Giza', 'New Cairo', 'Sheikh Zayed', '6th of October', 'Alexandria', 'North Coast (Sahel)', 'Ain Sokhna', 'El Gouna', 'Hurghada', 'Sharm El Sheikh', 'Luxor', 'Aswan', 'Mansoura', 'Tanta'],
  AE: ['Dubai', 'Abu Dhabi', 'Sharjah', 'Ras Al Khaimah'],
  SA: ['Riyadh', 'Jeddah', 'Dammam', 'Al Khobar'],
}

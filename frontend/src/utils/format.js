export const STATUS = {
  Backlogg: 'Backlogg',
  Planerad: 'Planerad',
  Pagaende: 'Pågående',
  Pausad: 'Pausad',
  Klar: 'Klar',
}
export const STATUS_ORDER = ['Backlogg', 'Planerad', 'Pagaende', 'Pausad', 'Klar']

export const ROLES = { Lasare: 'Läsare', Planerare: 'Planerare', Admin: 'Admin' }
export const OBJECT_TYPES = { Forvaltning: 'Förvaltningsobjekt', Projekt: 'Projekt' }
export const RESOURCE_TYPES = { Intern: 'Intern', Konsult: 'Konsult', Praktikant: 'Praktikant' }

const nf = new Intl.NumberFormat('sv-SE', { maximumFractionDigits: 0 })
export const fmt = (n) => (n === null || n === undefined || Number.isNaN(n) ? '–' : nf.format(n))
export const fmtH = (n) => `${fmt(n)} h`
export const fmtPct = (n) => (Number.isFinite(n) ? `${Math.round(n)} %` : '∞ %')

const dtf = new Intl.DateTimeFormat('sv-SE', { dateStyle: 'medium', timeStyle: 'short' })
const df = new Intl.DateTimeFormat('sv-SE', { dateStyle: 'medium' })
const tf = new Intl.DateTimeFormat('sv-SE', { timeStyle: 'short' })

export const fmtDateTime = (iso) => (iso ? dtf.format(new Date(iso)) : '–')
export const fmtDate = (iso) => (iso ? df.format(new Date(iso)) : '–')
export const fmtTime = (d) => tf.format(d instanceof Date ? d : new Date(d))

export function fmtRelative(iso) {
  if (!iso) return ''
  const diff = (Date.now() - new Date(iso).getTime()) / 1000
  if (diff < 45) return 'nyss'
  if (diff < 3600) return `${Math.round(diff / 60)} min sedan`
  if (diff < 86400) return `${Math.round(diff / 3600)} tim sedan`
  if (diff < 86400 * 7) return `${Math.round(diff / 86400)} d sedan`
  return fmtDate(iso)
}

/** Jämför versionsnummer "1.2.10" > "1.2.9". */
export function compareVersions(a = '0', b = '0') {
  const pa = String(a).split(/[.+-]/).map((x) => parseInt(x, 10) || 0)
  const pb = String(b).split(/[.+-]/).map((x) => parseInt(x, 10) || 0)
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] || 0) - (pb[i] || 0)
    if (d) return d > 0 ? 1 : -1
  }
  return 0
}

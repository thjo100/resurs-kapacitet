// Beräkningar för beläggning och fördelning. Rena funktioner – testas med vitest.

export const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAJ', 'JUN', 'JUL', 'AUG', 'SEP', 'OKT', 'NOV', 'DEC']
export const MONTH_NAMES = [
  'januari',
  'februari',
  'mars',
  'april',
  'maj',
  'juni',
  'juli',
  'augusti',
  'september',
  'oktober',
  'november',
  'december',
]

export const DEFAULT_THRESHOLDS = { thresholdLow: 80, thresholdHigh: 100, thresholdCritical: 110 }

export function pct(allocated, capacity) {
  if (!capacity) return allocated > 0 ? Infinity : 0
  return (allocated / capacity) * 100
}

/**
 * Beläggningsnivå för en cell.
 *  empty – inget planerat
 *  low   – under låg tröskel (ledig kapacitet)
 *  ok    – bra beläggning (låg–hög tröskel)
 *  near  – överbelagd (hög–kritisk tröskel)
 *  over  – kraftigt överbelagd (över kritisk tröskel, eller timmar utan kapacitet)
 */
export function occLevel(allocated, capacity, t = DEFAULT_THRESHOLDS) {
  if (!allocated) return 'empty'
  if (!capacity) return 'over'
  const p = pct(allocated, capacity)
  if (p < t.thresholdLow) return 'low'
  if (p <= t.thresholdHigh + 0.5 * (100 / capacity)) return 'ok'
  if (p <= t.thresholdCritical) return 'near'
  return 'over'
}

export const OCC_LABELS = {
  empty: 'inget planerat',
  low: 'ledig kapacitet',
  ok: 'bra beläggning',
  near: 'överbelagd',
  over: 'kraftigt överbelagd',
}

export const sum = (arr) => arr.reduce((a, b) => a + (Number(b) || 0), 0)

export function sumMonths(rows) {
  const out = new Array(12).fill(0)
  for (const r of rows) for (let m = 0; m < 12; m++) out[m] += Number(r[m]) || 0
  return out
}

/**
 * Fördelar `total` timmar i heltal över valda månader (index 0–11).
 * Med `weights` (t.ex. kapacitet per månad) viktas fördelningen så att t.ex. juli får mindre.
 * Avrundningsrest läggs på de tidigaste månaderna.
 */
export function distribute(total, months, weights = null) {
  const out = new Array(12).fill(0)
  total = Math.max(0, Math.round(Number(total) || 0))
  const idx = months.filter((m) => m >= 0 && m < 12)
  if (!idx.length || !total) return out

  let w = idx.map((m) => (weights ? Math.max(0, Number(weights[m]) || 0) : 1))
  let wsum = sum(w)
  if (wsum === 0) {
    w = idx.map(() => 1)
    wsum = idx.length
  }

  const exact = idx.map((m, i) => (total * w[i]) / wsum)
  const floors = exact.map(Math.floor)
  let rest = total - sum(floors)
  // Största rest först, vid lika – tidigaste månaden.
  const order = exact
    .map((v, i) => ({ i, frac: v - floors[i] }))
    .filter((x) => w[x.i] > 0)
    .sort((a, b) => b.frac - a.frac || a.i - b.i)
  for (let k = 0; rest > 0 && order.length; k = (k + 1) % order.length, rest--) floors[order[k].i]++
  idx.forEach((m, i) => (out[m] = floors[i]))
  return out
}

/** Fyll höger: kopierar värdet i månad `from` till alla månader efter. */
export function fillRight(hours, from) {
  return hours.map((h, i) => (i > from ? hours[from] : h))
}

/** Tolkar text inklistrad från Excel (tabbar/radbrytningar) till en matris av tal (null = tom). */
export function parseClipboard(text) {
  const rows = String(text ?? '')
    .replace(/\r\n?/g, '\n')
    .replace(/\n$/, '')
    .split('\n')
  return rows.map((r) =>
    r.split('\t').map((c) => {
      const v = c.trim().replace(/\s| /g, '').replace(',', '.')
      if (v === '') return null
      const n = Number(v)
      return Number.isFinite(n) ? Math.max(0, Math.round(n)) : NaN
    }),
  )
}

export function parseHours(value) {
  const v = String(value ?? '')
    .trim()
    .replace(/\s| /g, '')
    .replace(',', '.')
  if (v === '') return 0
  const n = Number(v)
  if (!Number.isFinite(n) || n < 0 || n > 744) return NaN
  return Math.round(n)
}

export function initials(name = '') {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0].toUpperCase())
    .join('')
}

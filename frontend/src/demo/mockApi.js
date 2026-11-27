// Demoläge: en webbläsarbaserad backend som följer samma regler som .NET-API:t.
// Används bara när appen byggs med `npm run build:demo` (VITE_DEMO=true). Data sparas i webbläsarens localStorage.
import { ApiError } from '../api.js'

// Release notes läggs in vid demobygget (se vite.config.js) – det vanliga bygget behöver inte backend-mappen.
// eslint-disable-next-line no-undef
const releaseNotes = typeof __RELEASE_NOTES__ !== 'undefined' ? __RELEASE_NOTES__ : []

const KEY = 'rk-demo-db-v1'
const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAJ', 'JUN', 'JUL', 'AUG', 'SEP', 'OKT', 'NOV', 'DEC']
const STATUS = { Backlogg: 'Backlogg', Planerad: 'Planerad', Pagaende: 'Pågående', Pausad: 'Pausad', Klar: 'Klar' }
const ROLES = { Lasare: 'Läsare', Planerare: 'Planerare', Admin: 'Admin' }
const startedAt = new Date().toISOString()
const nowIso = () => new Date().toISOString()

let db = load() ?? seed(new Date().getFullYear())

function load() {
  try {
    const s = localStorage.getItem(KEY)
    return s ? JSON.parse(s) : null
  } catch {
    return null
  }
}
function save() {
  try {
    localStorage.setItem(KEY, JSON.stringify(db))
  } catch {
    /* privat läge – data finns kvar tills sidan laddas om */
  }
}
const nextId = (c) => (db.seq[c] = (db.seq[c] ?? 0) + 1)
const fail = (msg, status = 400) => {
  throw new ApiError(msg, status)
}
const clean = (s) => (typeof s === 'string' && s.trim() ? s.trim() : null)

// ---------- Behörighet och logg ----------
const me = () => db.users.find((u) => u.id === db.session && u.isActive) ?? null
function requireRole(...roles) {
  const u = me()
  if (!u) fail('Du behöver logga in.', 401)
  if (!roles.includes(u.role)) fail('Du saknar behörighet för detta.', 403)
  return u
}
const planner = () => requireRole('Planerare', 'Admin')
const admin = () => requireRole('Admin')

function log(entityType, entityId, action, summary, extra = {}) {
  db.changes.push({
    id: nextId('changes'),
    timestamp: nowIso(),
    user: me()?.displayName ?? 'System',
    entityType,
    entityId: entityId ?? null,
    action,
    summary,
    resourceId: extra.resourceId ?? null,
    assignmentId: extra.assignmentId ?? null,
  })
}
function diff(...fields) {
  const fmt = (v) => (v === null || v === undefined || v === '' ? '–' : v === true ? 'ja' : v === false ? 'nej' : String(v))
  return fields
    .filter(([, a, b]) => fmt(a) !== fmt(b))
    .map(([l, a, b]) => `${l}: ${fmt(a)} → ${fmt(b)}`)
    .join(', ')
}
const withDiff = (text, d) => (d ? `${text}: ${d}` : text)

// ---------- Kapacitet (samma som CapacityCalculator.cs) ----------
function employed(r, y, m) {
  const first = new Date(Date.UTC(y, m - 1, 1))
  const last = new Date(Date.UTC(y, m, 0))
  if (r.startDate && new Date(r.startDate) > last) return false
  if (r.endDate && new Date(r.endDate) < first) return false
  return true
}
function capacity(r, y) {
  const s = db.settings
  const base = (r.capacityPerMonth ?? s.standardCapacity) * (r.employmentPercent / 100) * (s.planningCeilingPercent / 100)
  return MONTHS.map((_, i) => {
    const m = i + 1
    if (!employed(r, y, m)) return 0
    const o = db.overrides.find((x) => x.resourceId === r.id && x.year === y && x.month === m)
    return o ? o.hours : Math.round(base * s.monthFactors[i])
  })
}

// ---------- DTO:er ----------
const settingsDto = () => ({ ...db.settings, monthFactors: [...db.settings.monthFactors] })
function totals() {
  const asgOf = new Map(db.allocations.map((a) => [a.id, a.assignmentId]))
  const total = new Map()
  const byYear = new Map()
  for (const m of db.months) {
    const aid = asgOf.get(m.allocationId)
    total.set(aid, (total.get(aid) ?? 0) + m.hours)
    const k = `${aid}:${m.year}`
    byYear.set(k, (byYear.get(k) ?? 0) + m.hours)
  }
  return { total, byYear }
}
function asgDto(a, year, t = totals()) {
  const allocatedTotal = t.total.get(a.id) ?? 0
  return {
    ...a,
    allocatedTotal,
    allocatedYear: t.byYear.get(`${a.id}:${year}`) ?? 0,
    remaining: a.estimatedHours - allocatedTotal,
  }
}
function allocDto(a, year) {
  const hours = new Array(12).fill(0)
  let other = 0
  for (const m of db.months.filter((x) => x.allocationId === a.id)) {
    if (m.year === year) hours[m.month - 1] = m.hours
    else other += m.hours
  }
  return { id: a.id, resourceId: a.resourceId, assignmentId: a.assignmentId, note: a.note ?? null, hours, otherYearsHours: other }
}
function resourceDto(r, year) {
  return {
    ...r,
    capacity: capacity(r, year),
    absences: db.overrides
      .filter((o) => o.resourceId === r.id && o.year === year)
      .sort((a, b) => a.month - b.month)
      .map((o) => ({ month: o.month, hours: o.hours, reason: o.reason ?? null })),
  }
}
function relevant(a, year, other) {
  if (a.status === 'Klar') return false
  if (a.startDate && new Date(a.startDate).getFullYear() > year) return false
  if (a.endDate && new Date(a.endDate).getFullYear() < year) return false
  if (other > 0 && !a.startDate && !a.endDate) return false
  return true
}
function planning(year) {
  const t = totals()
  const asgById = new Map(db.assignments.map((a) => [a.id, a]))
  const allocations = db.allocations
    .map((a) => allocDto(a, year))
    .filter((a) => a.hours.some((h) => h) || relevant(asgById.get(a.assignmentId), year, a.otherYearsHours))
  return {
    year,
    settings: settingsDto(),
    teams: [...db.teams].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'sv')),
    resources: [...db.resources].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'sv')).map((r) => resourceDto(r, year)),
    objects: [...db.objects].sort((a, b) => a.name.localeCompare(b.name, 'sv')),
    assignments: [...db.assignments].sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name, 'sv')).map((a) => asgDto(a, year, t)),
    allocations,
    lastChangeId: db.changes.at(-1)?.id ?? 0,
  }
}
const meDto = (u) => (u ? { id: u.id, userName: u.userName, displayName: u.displayName, role: u.role, mustChangePassword: u.mustChangePassword } : null)
const userDto = (u) => ({ id: u.id, userName: u.userName, displayName: u.displayName, role: u.role, isActive: u.isActive, mustChangePassword: u.mustChangePassword, createdAt: u.createdAt, lastLoginAt: u.lastLoginAt })

// ---------- Hjälpare för celler ----------
function cell(allocationId, year, month) {
  return db.months.find((m) => m.allocationId === allocationId && m.year === year && m.month === month)
}
function setHours(allocationId, year, month, hours, add = false) {
  let c = cell(allocationId, year, month)
  if (!c) {
    if (!hours) return
    c = { allocationId, year, month, hours: 0 }
    db.months.push(c)
  }
  c.hours = add ? c.hours + hours : hours
  c.updatedAt = nowIso()
  c.updatedBy = me()?.displayName ?? 'System'
}
const nameOf = (list, id) => list.find((x) => x.id === id)?.name ?? null

// ---------- Import (samma format som PlanningImport.cs) ----------
function parseImport(text) {
  const rows = []
  const errors = []
  if (!text?.trim()) return { rows, errors: ['Ingen data att importera.'] }
  const lines = text.replace(/\r\n?/g, '\n').split('\n')
  const sample = lines.slice(0, 20).join('\n')
  const delim = sample.includes('\t') ? '\t' : (sample.match(/;/g)?.length ?? 0) >= (sample.match(/,/g)?.length ?? 0) ? ';' : ','
  const split = (line) => {
    const out = []
    let cur = ''
    let q = false
    for (let i = 0; i < line.length; i++) {
      const ch = line[i]
      if (ch === '"') {
        if (q && line[i + 1] === '"') (cur += '"'), i++
        else q = !q
      } else if (ch === delim && !q) out.push(cur), (cur = '')
      else cur += ch
    }
    out.push(cur)
    return out
  }
  const mn = MONTHS.map((m) => m.toLowerCase())
  let cols = null
  lines.forEach((line, i) => {
    const cells = split(line)
    if (cells.every((c) => !c.trim())) return
    if (!cols) {
      const idx = cells.findIndex((c) => c.trim().toLowerCase() === 'resurs')
      if (idx >= 0) {
        cols = { months: new Array(12).fill(null) }
        cells.forEach((c, j) => {
          const h = c.trim().toLowerCase()
          if (h === 'resurs') cols.resource = j
          else if (h === 'tillhörighet' || h === 'team') cols.team = j
          else if (h === 'uppdrag') cols.assignment = j
          else if (/^(förvaltning|objekt|projekt)/.test(h)) cols.object = j
          else {
            const m = mn.findIndex((x) => h.startsWith(x))
            if (m >= 0 && h.length <= 9) cols.months[m] = j
          }
        })
        return
      }
      const s = cells.findIndex((c) => c.trim())
      cols = { resource: s, team: s + 1, assignment: s + 2, object: s + 3, months: MONTHS.map((_, m) => s + 4 + m) }
    }
    const get = (c) => (c !== undefined && c !== null && c < cells.length ? cells[c].trim() : '')
    const resource = get(cols.resource)
    const assignment = get(cols.assignment)
    if (!resource && !assignment) return
    if (!resource) return errors.push(`Rad ${i + 1}: resurs saknas.`)
    if (!assignment) return errors.push(`Rad ${i + 1}: uppdrag saknas för ${resource}.`)
    const hours = new Array(12).fill(0)
    let ok = true
    cols.months.forEach((c, m) => {
      const raw = get(c).replace(/\s| /g, '')
      if (!raw) return
      const v = Number(raw.replace(',', '.'))
      if (Number.isFinite(v) && v >= 0 && v <= 744) hours[m] = Math.round(v)
      else (errors.push(`Rad ${i + 1}: ogiltigt värde '${raw}' i ${MONTHS[m]}.`), (ok = false))
    })
    if (ok) rows.push({ resource, team: get(cols.team) || null, assignment, object: get(cols.object) || null, hours })
  })
  return { rows, errors }
}

// ---------- Routes ----------
const routes = []
const on = (method, pattern, fn) => routes.push({ method, re: new RegExp(`^${pattern.replace(/:id/g, '(\\d+)')}$`), fn })

on('GET', '/api/health', () => ({ status: 'ok' }))
on('GET', '/api/version', () => ({
  version: releaseNotes[0]?.version ?? '0.0.0',
  commit: null,
  buildDate: null,
  environment: 'Demo',
  startedAt,
}))
on('GET', '/api/releases', () => releaseNotes)

on('GET', '/api/auth/me', () => meDto(me()))
on('POST', '/api/auth/login', (_, b) => {
  const u = db.users.find((x) => x.userName === String(b.userName ?? '').trim().toLowerCase() && x.isActive)
  if (!u || u.password !== b.password) fail('Fel användarnamn eller lösenord.', 401)
  u.lastLoginAt = nowIso()
  db.session = u.id
  return meDto(u)
})
on('POST', '/api/auth/logout', () => {
  db.session = null
  return null
})
on('POST', '/api/auth/password', (_, b) => {
  const u = requireRole('Lasare', 'Planerare', 'Admin')
  if (u.password !== b.currentPassword) fail('Nuvarande lösenord stämmer inte.')
  if ((b.newPassword ?? '').length < 6) fail('Lösenordet måste vara minst 6 tecken.')
  u.password = b.newPassword
  u.mustChangePassword = false
  log('Användare', u.id, 'Ändrad', `${u.displayName} bytte lösenord`)
  return null
})
on('POST', '/api/demo/reset', () => {
  db = seed(new Date().getFullYear())
  return null
})

on('GET', '/api/planning', (q) => planning(Number(q.get('year')) || new Date().getFullYear()))
on('GET', '/api/settings', () => settingsDto())
on('PUT', '/api/settings', (_, b) => {
  admin()
  const s = db.settings
  const before = settingsDto()
  const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, Number(v)))
  if (clean(b.organizationName)) s.organizationName = b.organizationName.trim()
  if (b.standardCapacity != null) s.standardCapacity = clamp(b.standardCapacity, 0, 400)
  if (b.planningCeilingPercent != null) s.planningCeilingPercent = clamp(b.planningCeilingPercent, 10, 100)
  if (Array.isArray(b.monthFactors) && b.monthFactors.length === 12) s.monthFactors = b.monthFactors.map((f) => clamp(f || 0, 0, 2))
  for (const k of ['thresholdLow', 'thresholdHigh', 'thresholdCritical']) if (b[k] != null) s[k] = clamp(b[k], 0, 300)
  if (b.requireLoginToView != null) s.requireLoginToView = !!b.requireLoginToView
  if (b.changeLogRetentionMonths != null) s.changeLogRetentionMonths = clamp(b.changeLogRetentionMonths, 1, 120)
  if (!(s.thresholdLow <= s.thresholdHigh && s.thresholdHigh <= s.thresholdCritical)) {
    Object.assign(s, before)
    fail('Tröskelvärdena måste vara i stigande ordning.')
  }
  log('Inställningar', 1, 'Ändrad', withDiff('Inställningar ändrades', diff(
    ['Standardkapacitet', before.standardCapacity, s.standardCapacity],
    ['Planeringstak %', before.planningCeilingPercent, s.planningCeilingPercent],
    ['Månadsfaktorer', before.monthFactors.join(' '), s.monthFactors.join(' ')],
    ['Trösklar', `${before.thresholdLow}/${before.thresholdHigh}/${before.thresholdCritical}`, `${s.thresholdLow}/${s.thresholdHigh}/${s.thresholdCritical}`],
  )))
  return settingsDto()
})

// Team
on('GET', '/api/teams', () => [...db.teams].sort((a, b) => a.sortOrder - b.sortOrder))
on('POST', '/api/teams', (_, b) => {
  admin()
  const name = clean(b.name) ?? fail('Teamet måste ha ett namn.')
  if (db.teams.some((t) => t.name === name)) fail(`Det finns redan ett team som heter ${name}.`, 409)
  const t = { id: nextId('teams'), name, lead: clean(b.lead), description: clean(b.description), color: b.color || '#4f6bed', sortOrder: b.sortOrder ?? db.teams.length + 1, isActive: b.isActive ?? true }
  db.teams.push(t)
  log('Team', t.id, 'Skapad', `Team ${t.name} skapades`)
  return t
})
on('PUT', '/api/teams/:id', (_, b, id) => {
  admin()
  const t = db.teams.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const name = clean(b.name) ?? fail('Teamet måste ha ett namn.')
  if (db.teams.some((x) => x.name === name && x.id !== id)) fail(`Det finns redan ett team som heter ${name}.`, 409)
  const d = diff(['Namn', t.name, name], ['Teamledare', t.lead, clean(b.lead)], ['Aktiv', t.isActive, b.isActive ?? t.isActive])
  Object.assign(t, { name, lead: clean(b.lead), description: clean(b.description), color: b.color || t.color, sortOrder: b.sortOrder ?? t.sortOrder, isActive: b.isActive ?? t.isActive })
  log('Team', id, 'Ändrad', withDiff(`Team ${t.name} ändrades`, d))
  return t
})
on('DELETE', '/api/teams/:id', (_, __, id) => {
  admin()
  const t = db.teams.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  db.teams = db.teams.filter((x) => x.id !== id)
  db.resources.forEach((r) => r.teamId === id && (r.teamId = null))
  log('Team', id, 'Borttagen', `Team ${t.name} togs bort (resurserna ligger kvar utan team)`)
  return null
})

// Resurser
function validResource(b) {
  if (!clean(b.name)) fail('Resursen måste ha ett namn.')
  if (b.employmentPercent != null && (b.employmentPercent < 0 || b.employmentPercent > 100)) fail('Sysselsättningsgraden måste vara 0–100 %.')
  if (b.capacityPerMonth != null && (b.capacityPerMonth < 0 || b.capacityPerMonth > 400)) fail('Kapaciteten måste vara 0–400 h/mån.')
  if (b.startDate && b.endDate && b.endDate < b.startDate) fail('Slutdatum kan inte vara före startdatum.')
}
const applyResource = (r, b) =>
  Object.assign(r, {
    name: b.name.trim(),
    teamId: b.teamId ?? null,
    role: clean(b.role),
    type: b.type ?? r.type ?? 'Intern',
    capacityPerMonth: b.capacityPerMonth ?? null,
    employmentPercent: b.employmentPercent ?? r.employmentPercent ?? 100,
    startDate: b.startDate || null,
    endDate: b.endDate || null,
    isActive: b.isActive ?? r.isActive ?? true,
    notes: clean(b.notes),
    sortOrder: b.sortOrder ?? r.sortOrder ?? db.resources.length + 1,
  })
on('GET', '/api/resources', (q) => db.resources.map((r) => resourceDto(r, Number(q.get('year')) || new Date().getFullYear())))
on('POST', '/api/resources', (_, b) => {
  admin()
  validResource(b)
  const r = applyResource({ id: nextId('resources') }, b)
  db.resources.push(r)
  log('Resurs', r.id, 'Skapad', `Resurs ${r.name} skapades`, { resourceId: r.id })
  return resourceDto(r, new Date().getFullYear())
})
on('PUT', '/api/resources/:id', (_, b, id) => {
  admin()
  validResource(b)
  const r = db.resources.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const d = diff(
    ['Namn', r.name, b.name.trim()],
    ['Team', nameOf(db.teams, r.teamId), nameOf(db.teams, b.teamId)],
    ['Roll', r.role, clean(b.role)],
    ['Kapacitet', r.capacityPerMonth, b.capacityPerMonth ?? null],
    ['Sysselsättning %', r.employmentPercent, b.employmentPercent ?? r.employmentPercent],
    ['Start', r.startDate, b.startDate || null],
    ['Slut', r.endDate, b.endDate || null],
    ['Aktiv', r.isActive, b.isActive ?? r.isActive],
  )
  applyResource(r, b)
  log('Resurs', id, 'Ändrad', withDiff(`Resurs ${r.name} ändrades`, d), { resourceId: id })
  return resourceDto(r, new Date().getFullYear())
})
on('DELETE', '/api/resources/:id', (_, __, id) => {
  admin()
  const r = db.resources.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const ids = db.allocations.filter((a) => a.resourceId === id).map((a) => a.id)
  db.months = db.months.filter((m) => !ids.includes(m.allocationId))
  db.allocations = db.allocations.filter((a) => a.resourceId !== id)
  db.overrides = db.overrides.filter((o) => o.resourceId !== id)
  db.resources = db.resources.filter((x) => x.id !== id)
  log('Resurs', id, 'Borttagen', `Resurs ${r.name} togs bort${ids.length ? ` (${ids.length} uppdragsrader togs också bort)` : ''}`, { resourceId: id })
  return null
})
on('PUT', '/api/resources/:id/absences', (_, b, id) => {
  planner()
  if (b.month < 1 || b.month > 12) fail('Ogiltig månad.')
  const r = db.resources.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const label = `${MONTHS[b.month - 1]} ${b.year}`
  const existing = db.overrides.find((o) => o.resourceId === id && o.year === b.year && o.month === b.month)
  if (b.hours === null || b.hours === undefined) {
    if (!existing) return null
    db.overrides = db.overrides.filter((o) => o !== existing)
    log('Kapacitet', id, 'Borttagen', `${r.name}: avvikande kapacitet ${label} togs bort`, { resourceId: id })
  } else {
    const o = existing ?? (db.overrides.push({ resourceId: id, year: b.year, month: b.month }), db.overrides.at(-1))
    o.hours = Math.max(0, Math.min(744, Number(b.hours)))
    o.reason = clean(b.reason)
    log('Kapacitet', id, 'Ändrad', `${r.name}: kapacitet ${label} satt till ${o.hours} h${o.reason ? ` (${o.reason})` : ''}`, { resourceId: id })
  }
  return null
})

// Objekt
on('GET', '/api/objects', () => [...db.objects].sort((a, b) => a.name.localeCompare(b.name, 'sv')))
on('POST', '/api/objects', (_, b) => {
  admin()
  const name = clean(b.name) ?? fail('Objektet måste ha ett namn.')
  if (db.objects.some((o) => o.name === name)) fail(`Det finns redan ett objekt som heter ${name}.`, 409)
  const o = { id: nextId('objects'), name, type: b.type ?? 'Forvaltning', owner: clean(b.owner), budgetHoursPerYear: b.budgetHoursPerYear ?? null, description: clean(b.description), color: b.color || '#0f9d8a', isActive: b.isActive ?? true }
  db.objects.push(o)
  log('Objekt', o.id, 'Skapad', `${o.type === 'Projekt' ? 'Projekt' : 'Förvaltningsobjekt'} ${o.name} skapades`)
  return o
})
on('PUT', '/api/objects/:id', (_, b, id) => {
  admin()
  const o = db.objects.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const name = clean(b.name) ?? fail('Objektet måste ha ett namn.')
  if (db.objects.some((x) => x.name === name && x.id !== id)) fail(`Det finns redan ett objekt som heter ${name}.`, 409)
  const d = diff(['Namn', o.name, name], ['Ägare', o.owner, clean(b.owner)], ['Budget h/år', o.budgetHoursPerYear, b.budgetHoursPerYear ?? null], ['Aktiv', o.isActive, b.isActive ?? o.isActive])
  Object.assign(o, { name, type: b.type ?? o.type, owner: clean(b.owner), budgetHoursPerYear: b.budgetHoursPerYear ?? null, description: clean(b.description), color: b.color || o.color, isActive: b.isActive ?? o.isActive })
  log('Objekt', id, 'Ändrad', withDiff(`${o.name} ändrades`, d))
  return o
})
on('DELETE', '/api/objects/:id', (_, __, id) => {
  admin()
  const o = db.objects.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  db.objects = db.objects.filter((x) => x.id !== id)
  db.assignments.forEach((a) => a.objectId === id && (a.objectId = null))
  log('Objekt', id, 'Borttagen', `${o.name} togs bort (uppdragen ligger kvar utan objekt)`)
  return null
})

// Uppdrag
function validAssignment(b) {
  if (!clean(b.name)) fail('Uppdraget måste ha ett namn.')
  if (b.estimatedHours != null && (b.estimatedHours < 0 || b.estimatedHours > 1e6)) fail('Ogiltigt estimat.')
  if (b.startDate && b.endDate && b.endDate < b.startDate) fail('Slutdatum kan inte vara före startdatum.')
}
const applyAssignment = (a, b) =>
  Object.assign(a, {
    name: b.name.trim(),
    objectId: b.objectId ?? null,
    estimatedHours: b.estimatedHours ?? a.estimatedHours ?? 0,
    startDate: b.startDate || null,
    endDate: b.endDate || null,
    requester: clean(b.requester),
    responsible: clean(b.responsible),
    description: clean(b.description),
  })
on('GET', '/api/assignments', () => [...db.assignments].sort((a, b) => a.priority - b.priority).map((a) => asgDto(a, 0)))
on('POST', '/api/assignments', (_, b) => {
  planner()
  validAssignment(b)
  const a = applyAssignment({ id: nextId('assignments'), createdAt: nowIso() }, b)
  a.status = b.status ?? 'Backlogg'
  a.priority = b.priority ?? Math.max(0, ...db.assignments.map((x) => x.priority)) + 1
  db.assignments.push(a)
  log('Uppdrag', a.id, 'Skapad', `Uppdrag ${a.name} skapades (${a.estimatedHours} h, ${STATUS[a.status]})`, { assignmentId: a.id })
  return asgDto(a, 0)
})
on('PUT', '/api/assignments/order', (_, ids) => {
  planner()
  const byId = new Map(db.assignments.map((a) => [a.id, a]))
  let moved = false
  ids.forEach((id, i) => {
    const a = byId.get(id)
    if (a) (moved ||= a.priority !== i + 1), (a.priority = i + 1)
  })
  db.assignments.filter((a) => !ids.includes(a.id)).sort((a, b) => a.priority - b.priority).forEach((a, i) => (a.priority = ids.length + i + 1))
  if (moved) log('Backlogg', null, 'Omprioriterad', `Backloggen omprioriterades. Topp 3: ${ids.slice(0, 3).map((id) => byId.get(id)?.name).filter(Boolean).join(', ')}`)
  return null
})
on('PUT', '/api/assignments/:id', (_, b, id) => {
  planner()
  validAssignment(b)
  const a = db.assignments.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const status = b.status ?? a.status
  const d = diff(
    ['Namn', a.name, b.name.trim()],
    ['Objekt', nameOf(db.objects, a.objectId), nameOf(db.objects, b.objectId)],
    ['Estimat', a.estimatedHours, b.estimatedHours ?? a.estimatedHours],
    ['Status', STATUS[a.status], STATUS[status]],
    ['Beställare', a.requester, clean(b.requester)],
  )
  applyAssignment(a, b)
  a.status = status
  a.priority = b.priority ?? a.priority
  log('Uppdrag', id, 'Ändrad', withDiff(`Uppdrag ${a.name} ändrades`, d), { assignmentId: id })
  return asgDto(a, 0)
})
on('DELETE', '/api/assignments/:id', (_, __, id) => {
  planner()
  const a = db.assignments.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const ids = db.allocations.filter((x) => x.assignmentId === id).map((x) => x.id)
  db.months = db.months.filter((m) => !ids.includes(m.allocationId))
  db.allocations = db.allocations.filter((x) => x.assignmentId !== id)
  db.assignments = db.assignments.filter((x) => x.id !== id)
  log('Uppdrag', id, 'Borttagen', `Uppdrag ${a.name} togs bort${ids.length ? ` (${ids.length} resursrader togs också bort)` : ''}`, { assignmentId: id })
  return null
})

// Allokeringar
on('POST', '/api/allocations', (_, b) => {
  planner()
  const r = db.resources.find((x) => x.id === b.resourceId)
  const a = db.assignments.find((x) => x.id === b.assignmentId)
  if (!r || !a) fail('Resursen eller uppdraget finns inte.', 404)
  const year = b.year ?? new Date().getFullYear()
  let al = db.allocations.find((x) => x.resourceId === r.id && x.assignmentId === a.id)
  const isNew = !al
  if (!al) db.allocations.push((al = { id: nextId('allocations'), resourceId: r.id, assignmentId: a.id, note: clean(b.note), createdAt: nowIso() }))
  let total = 0
  ;(b.hours ?? []).slice(0, 12).forEach((h, i) => {
    h = Math.max(0, Math.min(744, Number(h) || 0))
    if (h) setHours(al.id, year, i + 1, h, true), (total += h)
  })
  let note = ''
  if (a.status === 'Backlogg') (a.status = 'Planerad'), (note = ' – uppdraget flyttades från backloggen till Planerad')
  log('Allokering', null, isNew ? 'Tilldelad' : 'Ändrad', `${a.name} tilldelades ${r.name}${total ? ` (${total} h ${year})` : ''}${note}`, { resourceId: r.id, assignmentId: a.id })
  return allocDto(al, year)
})
on('PUT', '/api/allocations/cells', (_, b) => {
  planner()
  const year = b.year
  if ((b.cells ?? []).some((c) => c.month < 1 || c.month > 12)) fail('Ogiltig månad.')
  if ((b.cells ?? []).some((c) => c.hours < 0 || c.hours > 744)) fail('Timmar måste vara mellan 0 och 744.')
  const conflicts = []
  const changes = new Map()
  for (const c of b.cells ?? []) {
    const al = db.allocations.find((x) => x.id === c.allocationId)
    if (!al) continue
    const existing = cell(al.id, year, c.month)
    const current = existing?.hours ?? 0
    if (!b.force && c.expected != null && c.expected !== current && current !== c.hours) {
      conflicts.push({ allocationId: al.id, month: c.month, current, attempted: c.hours, changedBy: existing?.updatedBy ?? null, changedAt: existing?.updatedAt ?? null })
      continue
    }
    if (current === c.hours) continue
    setHours(al.id, year, c.month, c.hours)
    if (!changes.has(al.id)) changes.set(al.id, [])
    changes.get(al.id).push(`${MONTHS[c.month - 1]} ${current}→${c.hours}`)
  }
  for (const [id, list] of changes) {
    const al = db.allocations.find((x) => x.id === id)
    const a = db.assignments.find((x) => x.id === al.assignmentId)
    if (a?.status === 'Backlogg') a.status = 'Planerad'
    log('Allokering', id, 'Ändrad', `${nameOf(db.resources, al.resourceId)} · ${a?.name} (${year}): ${list.join(', ')}`, { resourceId: al.resourceId, assignmentId: al.assignmentId })
  }
  const ids = [...new Set((b.cells ?? []).map((c) => c.allocationId))]
  return { updated: ids.map((id) => db.allocations.find((x) => x.id === id)).filter(Boolean).map((al) => allocDto(al, year)), conflicts }
})
on('PUT', '/api/allocations/:id/move', (_, b, id) => {
  planner()
  const src = db.allocations.find((x) => x.id === id)
  const target = db.resources.find((x) => x.id === b.resourceId)
  if (!src || !target) fail('Hittades inte.', 404)
  if (src.resourceId === target.id) return { id }
  const from = nameOf(db.resources, src.resourceId)
  const existing = db.allocations.find((x) => x.resourceId === target.id && x.assignmentId === src.assignmentId)
  let resultId = src.id
  if (!existing) src.resourceId = target.id
  else {
    for (const m of db.months.filter((x) => x.allocationId === src.id)) setHours(existing.id, m.year, m.month, m.hours, true)
    db.months = db.months.filter((x) => x.allocationId !== src.id)
    db.allocations = db.allocations.filter((x) => x !== src)
    resultId = existing.id
  }
  log('Allokering', resultId, 'Flyttad', `${nameOf(db.assignments, src.assignmentId)} flyttades från ${from} till ${target.name}${existing ? ' (timmarna slogs ihop med befintlig rad)' : ''}`, { resourceId: target.id, assignmentId: src.assignmentId })
  return { id: resultId }
})
on('PUT', '/api/allocations/:id/note', (_, b, id) => {
  planner()
  const al = db.allocations.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  al.note = clean(b.note)
  log('Allokering', id, 'Ändrad', `${nameOf(db.resources, al.resourceId)} · ${nameOf(db.assignments, al.assignmentId)}: kommentar ändrad`, { resourceId: al.resourceId, assignmentId: al.assignmentId })
  return null
})
on('DELETE', '/api/allocations/:id', (_, __, id) => {
  planner()
  const al = db.allocations.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const hours = db.months.filter((m) => m.allocationId === id).reduce((s, m) => s + m.hours, 0)
  db.months = db.months.filter((m) => m.allocationId !== id)
  db.allocations = db.allocations.filter((x) => x.id !== id)
  log('Allokering', id, 'Borttagen', `${nameOf(db.assignments, al.assignmentId)} togs bort från ${nameOf(db.resources, al.resourceId)}${hours ? ` (${hours} h totalt)` : ''}`, { resourceId: al.resourceId, assignmentId: al.assignmentId })
  return null
})
on('POST', '/api/allocations/copy-year', (_, b) => {
  planner()
  if (b.fromYear === b.toYear) fail('Välj två olika år.')
  const src = db.months.filter((m) => m.year === b.fromYear && m.hours > 0)
  if (db.months.some((m) => m.year === b.toYear && m.hours > 0) && !b.overwrite)
    fail(`${b.toYear} har redan planerade timmar. Välj att skriva över om du vill fortsätta.`, 409)
  db.months = db.months.filter((m) => m.year !== b.toYear)
  src.forEach((m) => db.months.push({ ...m, year: b.toYear, updatedAt: nowIso(), updatedBy: me()?.displayName }))
  const copied = src.reduce((s, m) => s + m.hours, 0)
  log('Planering', null, 'Kopierad', `Planen för ${b.fromYear} kopierades till ${b.toYear} (${copied} h)`)
  return { copiedHours: copied }
})

// Ändringslogg
on('GET', '/api/changes', (q) => {
  let list = [...db.changes].reverse()
  const before = Number(q.get('before'))
  if (before) list = list.filter((c) => c.id < before)
  if (q.get('entityType')) list = list.filter((c) => c.entityType === q.get('entityType'))
  if (q.get('resourceId')) list = list.filter((c) => c.resourceId === Number(q.get('resourceId')))
  if (q.get('assignmentId')) list = list.filter((c) => c.assignmentId === Number(q.get('assignmentId')))
  const term = q.get('q')?.trim().toLowerCase()
  if (term) list = list.filter((c) => `${c.summary} ${c.user}`.toLowerCase().includes(term))
  return list.slice(0, Math.min(500, Number(q.get('take')) || 50))
})
on('GET', '/api/changes/latest', () => {
  const c = db.changes.at(-1)
  return c ? { id: c.id, timestamp: c.timestamp, user: c.user, summary: c.summary } : null
})

// Användare
on('GET', '/api/users', () => (admin(), [...db.users].sort((a, b) => a.displayName.localeCompare(b.displayName, 'sv')).map(userDto)))
on('POST', '/api/users', (_, b) => {
  admin()
  const userName = String(b.userName ?? '').trim().toLowerCase()
  if (userName.length < 2) fail('Användarnamnet måste vara minst 2 tecken.')
  if ((b.password ?? '').length < 6) fail('Lösenordet måste vara minst 6 tecken.')
  if (db.users.some((u) => u.userName === userName)) fail(`Användarnamnet ${userName} är upptaget.`, 409)
  const u = { id: nextId('users'), userName, displayName: clean(b.displayName) ?? userName, role: b.role ?? 'Lasare', isActive: b.isActive ?? true, mustChangePassword: b.mustChangePassword ?? true, password: b.password, createdAt: nowIso(), lastLoginAt: null }
  db.users.push(u)
  log('Användare', u.id, 'Skapad', `Användare ${u.displayName} (${ROLES[u.role]}) skapades`)
  return userDto(u)
})
on('PUT', '/api/users/:id', (_, b, id) => {
  admin()
  const u = db.users.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  const role = b.role ?? u.role
  const active = b.isActive ?? u.isActive
  if (u.role === 'Admin' && (role !== 'Admin' || !active) && db.users.filter((x) => x.role === 'Admin' && x.isActive).length <= 1)
    fail('Det måste finnas minst en aktiv admin.')
  if (b.password && b.password.length < 6) fail('Lösenordet måste vara minst 6 tecken.')
  const d = diff(['Namn', u.displayName, clean(b.displayName) ?? u.displayName], ['Roll', ROLES[u.role], ROLES[role]], ['Aktiv', u.isActive, active])
  Object.assign(u, { displayName: clean(b.displayName) ?? u.displayName, role, isActive: active, mustChangePassword: b.mustChangePassword ?? u.mustChangePassword })
  if (b.password) u.password = b.password
  log('Användare', id, 'Ändrad', withDiff(`Användare ${u.displayName} ändrades`, d + (b.password ? `${d ? ', ' : ''}lösenord återställt` : '')))
  return userDto(u)
})
on('DELETE', '/api/users/:id', (_, __, id) => {
  const current = admin()
  const u = db.users.find((x) => x.id === id) ?? fail('Hittades inte.', 404)
  if (current.id === id) fail('Du kan inte ta bort dig själv.')
  if (u.role === 'Admin' && db.users.filter((x) => x.role === 'Admin' && x.isActive).length <= 1) fail('Det måste finnas minst en aktiv admin.')
  db.users = db.users.filter((x) => x.id !== id)
  log('Användare', id, 'Borttagen', `Användare ${u.displayName} togs bort`)
  return null
})

// Import
on('POST', '/api/import/planning', (_, b) => {
  admin()
  const { rows, errors } = parseImport(b.text)
  const result = { rows: rows.length, cells: 0, hours: 0, newTeams: [], newResources: [], newObjects: [], newAssignments: [], errors, applied: !b.dryRun }
  if (!rows.length) return { ...result, errors: errors.length ? errors : ['Inga rader hittades.'], applied: false }
  const snapshot = JSON.stringify(db)
  const same = (a, x) => a.trim().toLowerCase() === String(x ?? '').trim().toLowerCase()
  const sums = new Map()
  for (const row of rows) {
    let team = null
    if (row.team) {
      team = db.teams.find((t) => same(t.name, row.team))
      if (!team) db.teams.push((team = { id: nextId('teams'), name: row.team, color: '#4f6bed', sortOrder: db.teams.length + 1, isActive: true })), result.newTeams.push(row.team)
    }
    let r = db.resources.find((x) => same(x.name, row.resource))
    if (!r) db.resources.push((r = applyResource({ id: nextId('resources') }, { name: row.resource, teamId: team?.id ?? null }))), result.newResources.push(row.resource)
    let o = null
    if (row.object) {
      o = db.objects.find((x) => same(x.name, row.object))
      if (!o) db.objects.push((o = { id: nextId('objects'), name: row.object, type: /^projekt/i.test(row.object) ? 'Projekt' : 'Forvaltning', color: '#0f9d8a', isActive: true })), result.newObjects.push(row.object)
    }
    let a = db.assignments.find((x) => same(x.name, row.assignment) && (!o || x.objectId === o.id))
    if (!a) {
      a = { id: nextId('assignments'), name: row.assignment, objectId: o?.id ?? null, estimatedHours: 0, status: 'Pagaende', priority: Math.max(0, ...db.assignments.map((x) => x.priority)) + 1, createdAt: nowIso(), _new: true }
      db.assignments.push(a)
      result.newAssignments.push(row.assignment + (o ? ` (${o.name})` : ''))
    }
    const k = `${r.id}:${a.id}`
    if (!sums.has(k)) sums.set(k, { r, a, h: new Array(12).fill(0) })
    row.hours.forEach((h, m) => (sums.get(k).h[m] += h))
  }
  for (const { r, a, h } of sums.values()) {
    let al = db.allocations.find((x) => x.resourceId === r.id && x.assignmentId === a.id)
    if (!al) db.allocations.push((al = { id: nextId('allocations'), resourceId: r.id, assignmentId: a.id, createdAt: nowIso() }))
    h.forEach((v, m) => {
      if ((cell(al.id, b.year, m + 1)?.hours ?? 0) !== v) (result.cells++, setHours(al.id, b.year, m + 1, v))
    })
    result.hours += h.reduce((s, v) => s + v, 0)
    if (a._new) a.estimatedHours += h.reduce((s, v) => s + v, 0)
  }
  db.assignments.forEach((a) => delete a._new)
  if (b.dryRun) {
    db = JSON.parse(snapshot)
    return result
  }
  log('Import', null, 'Importerad', `Import ${b.year}: ${rows.length} rader, ${result.cells} celler, ${result.hours} h. Nya: ${result.newResources.length} resurser, ${result.newTeams.length} team, ${result.newObjects.length} objekt, ${result.newAssignments.length} uppdrag`)
  return result
})

export async function mockRequest(method, url, body) {
  await new Promise((r) => setTimeout(r, 60))
  const u = new URL(url, 'http://demo.local')
  for (const route of routes) {
    if (route.method !== method) continue
    const m = u.pathname.match(route.re)
    if (!m) continue
    const isRead = method === 'GET' && !u.pathname.startsWith('/api/auth') && !['/api/version', '/api/releases', '/api/health'].includes(u.pathname)
    if (isRead && db.settings.requireLoginToView && !me()) fail('Du behöver logga in.', 401)
    const before = JSON.stringify(db)
    try {
      const result = route.fn(u.searchParams, body ?? {}, m[1] ? Number(m[1]) : undefined)
      if (method !== 'GET') save()
      return result === undefined ? null : JSON.parse(JSON.stringify(result))
    } catch (e) {
      if (method !== 'GET') db = JSON.parse(before)
      throw e
    }
  }
  fail('Hittades inte.', 404)
}

// ---------- Demodata (samma som Seed.cs) ----------
function seed(year) {
  const d = {
    seq: {},
    session: null,
    settings: {
      organizationName: 'Systemutveckling',
      standardCapacity: 160,
      planningCeilingPercent: 90,
      monthFactors: [1, 1, 1, 1, 1, 0.9, 0.25, 0.9, 1, 1, 1, 0.85],
      thresholdLow: 80,
      thresholdHigh: 100,
      thresholdCritical: 110,
      requireLoginToView: false,
      changeLogRetentionMonths: 24,
    },
    teams: [],
    resources: [],
    overrides: [],
    objects: [],
    assignments: [],
    allocations: [],
    months: [],
    changes: [],
    users: [],
  }
  const id = (c) => (d.seq[c] = (d.seq[c] ?? 0) + 1)
  const t = nowIso()
  d.users.push(
    { id: id('users'), userName: 'admin', displayName: 'Administratör', role: 'Admin', isActive: true, mustChangePassword: false, password: 'admin', createdAt: t, lastLoginAt: null },
    { id: id('users'), userName: 'planerare', displayName: 'Pia Planerare', role: 'Planerare', isActive: true, mustChangePassword: false, password: 'planerare', createdAt: t, lastLoginAt: null },
  )
  const team = (name, lead, color, description) => {
    const x = { id: id('teams'), name, lead, color, description, sortOrder: d.teams.length + 1, isActive: true }
    d.teams.push(x)
    return x
  }
  const t1 = team('Team 1', 'Anna Svensson', '#4f6bed', 'Plattform och kärnsystem')
  const t2 = team('Team 2', 'Sara Nilsson', '#0f9d8a', 'Integration och e-tjänster')
  const t3 = team('Team 3', 'Lisa Berg', '#c2410c', 'Webb och UX')
  const obj = (name, type, owner, color, budget = null) => {
    const x = { id: id('objects'), name, type, owner, color, budgetHoursPerYear: budget, description: null, isActive: true }
    d.objects.push(x)
    return x
  }
  const areaA = obj('Område A', 'Forvaltning', 'Karin Holm', '#4f6bed')
  const areaB = obj('Område B', 'Forvaltning', 'Per Ström', '#0f9d8a')
  const areaC = obj('Område C', 'Forvaltning', 'Karin Holm', '#7c3aed')
  const nova = obj('Projekt Nova', 'Projekt', 'Ola Dahl', '#c2410c', 4000)
  const res = (name, tm, role, type = 'Intern', employment = 100) => {
    const x = { id: id('resources'), name, teamId: tm.id, role, type, capacityPerMonth: null, employmentPercent: employment, startDate: null, endDate: null, isActive: true, notes: null, sortOrder: d.resources.length + 1 }
    d.resources.push(x)
    return x
  }
  const kalle = res('Kalle Anka', t1, 'Systemutvecklare')
  const anna = res('Anna Svensson', t1, 'Lösningsarkitekt')
  const erik = res('Erik Lindqvist', t1, 'Systemutvecklare', 'Konsult')
  const joe = res('Joe Doe', t2, 'Systemutvecklare')
  const sara = res('Sara Nilsson', t2, 'Testledare')
  const mohammed = res('Mohammed Ali', t2, 'Systemutvecklare')
  const lisa = res('Lisa Berg', t3, 'UX-designer')
  const johan = res('Johan Ek', t3, 'Systemutvecklare', 'Intern', 80)
  const asg = (name, o, estimate, status, requester = null) => {
    const x = { id: id('assignments'), name, objectId: o.id, estimatedHours: estimate, status, priority: d.assignments.length + 1, startDate: null, endDate: null, requester, responsible: null, description: null, createdAt: t }
    d.assignments.push(x)
    return x
  }
  const x = asg('UppdragX', areaA, 1800, 'Pagaende', 'Karin Holm')
  const y = asg('UppdragY', areaA, 2400, 'Pagaende', 'Karin Holm')
  const z = asg('UppdragZ', areaB, 300, 'Pagaende', 'Per Ström')
  const portal = asg('Kundportal 2.0', nova, 4600, 'Pagaende', 'Ola Dahl')
  const integration = asg('Integrationsplattform', areaB, 1600, 'Planerad', 'Per Ström')
  asg('Ny inloggningstjänst (e-legitimation)', areaA, 400, 'Backlogg', 'Karin Holm')
  asg('Migrering till .NET 10', areaB, 600, 'Backlogg', 'Per Ström')
  asg('Tillgänglighetsanpassning webb', nova, 320, 'Backlogg', 'Ola Dahl')
  asg('Rapportmodul', areaC, 250, 'Backlogg', 'Karin Holm')
  asg('API för öppna data', areaC, 180, 'Backlogg')
  const alloc = (r, a, ...hours) => {
    const al = { id: id('allocations'), resourceId: r.id, assignmentId: a.id, note: null, createdAt: t }
    d.allocations.push(al)
    hours.forEach((h, i) => h && d.months.push({ allocationId: al.id, year, month: i + 1, hours: h, updatedAt: t, updatedBy: 'Demodata' }))
  }
  const _ = 0
  alloc(kalle, x, 100, 100, 100, 100, 100, 50, _, 75, 75, 75, 75, 75)
  alloc(kalle, y, 50, 50, 50, 50, 50, 20, _, 75, 75, 75, 75, 75)
  alloc(joe, z, 20, 20, 20, 20, 20, 20)
  alloc(joe, y, 90, 90, 90, 90, 90, 90, _, 90, 90, 90)
  alloc(anna, portal, 80, 80, 80, 80, 80, 70, 20, 70, 80, 80, 80, 70)
  alloc(anna, integration, 40, 40, 40, 40, 40, 30, _, 40, 40, 40, 40, 30)
  alloc(erik, portal, 140, 140, 140, 140, 140, 130, 40, 130, 140, 140, 140, 120)
  alloc(sara, x, 60, 60, 60, 60, 60, 50, _, 50, 60, 60, 60, 50)
  alloc(sara, portal, 60, 60, 60, 60, 60, 50, _, 50, 60, 30, 60, 50)
  alloc(mohammed, integration, _, _, 120, 120, 120, 110, 30, 110, 120, 120, 120, 100)
  alloc(lisa, portal, 100, 100, 100, 100, 100, 90, _, 90, 100, 100, 100, 90)
  alloc(johan, y, 60, 60, 60, 60, 60, 50, _, 50, 60, 60, 60, 50)
  alloc(johan, z, 40, 40, 40, 40, 40)
  d.overrides.push(
    { resourceId: kalle.id, year, month: 7, hours: 0, reason: 'Semester' },
    { resourceId: sara.id, year, month: 10, hours: 60, reason: 'Utbildning' },
    { resourceId: lisa.id, year, month: 7, hours: 0, reason: 'Semester' },
  )
  d.changes.push({ id: id('changes'), timestamp: t, user: 'System', entityType: 'System', entityId: null, action: 'Skapad', summary: `Demodata skapades för ${year} (3 team, 8 resurser, 10 uppdrag)`, resourceId: null, assignmentId: null })
  return d
}

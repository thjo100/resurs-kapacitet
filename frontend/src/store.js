import { reactive, computed } from 'vue'
import { api } from './api.js'
import { sum } from './utils/calc.js'
import { compareVersions } from './utils/format.js'

const readLS = (k, d = null) => {
  try {
    return localStorage.getItem(k) ?? d
  } catch {
    return d
  }
}
const writeLS = (k, v) => {
  try {
    localStorage.setItem(k, v)
  } catch {
    /* privat läge m.m. */
  }
}

export const store = reactive({
  me: null,
  meLoaded: false,
  version: null,
  releases: [],
  year: Number(readLS('rk-year')) || new Date().getFullYear(),
  planning: null,
  planningYear: null,
  loading: false,
  loadError: null,
  save: { state: 'idle', at: null, error: null },
  toasts: [],
  theme: document.documentElement.getAttribute('data-theme') || 'light',
  remoteChange: null,
  newVersion: null,
  lastSeenVersion: readLS('rk-last-seen-version', ''),
  focus: null, // { allocationId, month } – aktiv cell i planeringen
})

export const canEdit = computed(() => store.me?.role === 'Planerare' || store.me?.role === 'Admin')
export const isAdmin = computed(() => store.me?.role === 'Admin')

// ---------- Toasts ----------
let toastId = 0
export function toast(message, opts = {}) {
  const t = { id: ++toastId, message, type: opts.type || 'info', action: opts.action || null }
  store.toasts.push(t)
  if (store.toasts.length > 3) store.toasts.shift()
  const timeout = opts.timeout ?? (t.type === 'error' ? 0 : t.action ? 6000 : 4000)
  if (timeout) setTimeout(() => dismissToast(t.id), timeout)
  return t.id
}
export function dismissToast(id) {
  const i = store.toasts.findIndex((t) => t.id === id)
  if (i >= 0) store.toasts.splice(i, 1)
}
export const toastError = (e) => toast(e?.message || String(e), { type: 'error' })

// ---------- Tema ----------
export function setTheme(theme) {
  store.theme = theme
  document.documentElement.setAttribute('data-theme', theme)
  writeLS('rk-theme', theme)
}

// ---------- Inloggning ----------
export async function loadMe() {
  try {
    store.me = await api.get('/api/auth/me')
  } catch {
    store.me = null
  } finally {
    store.meLoaded = true
  }
}
export async function login(userName, password) {
  store.me = await api.post('/api/auth/login', { userName, password })
  return store.me
}
export async function logout() {
  await flushSaves()
  await api.post('/api/auth/logout')
  store.me = null
  toast('Du är utloggad.')
}

// ---------- Version & nyheter ----------
export async function loadVersion() {
  try {
    const [v, releases] = await Promise.all([api.get('/api/version'), api.get('/api/releases')])
    store.version = v
    store.releases = releases || []
  } catch {
    /* ignoreras */
  }
}
export const unseenReleases = computed(() =>
  store.releases.filter((r) => !store.lastSeenVersion || compareVersions(r.version, store.lastSeenVersion) > 0),
)
export function markReleasesSeen() {
  const latest = store.releases[0]?.version
  if (latest) {
    store.lastSeenVersion = latest
    writeLS('rk-last-seen-version', latest)
  }
}

// ---------- Planering ----------
export function setYear(y) {
  store.year = y
  writeLS('rk-year', String(y))
}

function prepareAllocation(a) {
  a._server = [...a.hours]
  return a
}

export async function loadPlanning(year = store.year, { silent = false } = {}) {
  if (!silent) store.loading = true
  store.loadError = null
  try {
    await flushSaves()
    const p = await api.get(`/api/planning?year=${year}`)
    p.allocations.forEach(prepareAllocation)
    // Behåll lokala osparade ändringar om sådana hunnit uppstå under laddningen.
    for (const [key, pend] of pending) {
      const a = p.allocations.find((x) => x.id === pend.allocationId)
      if (a && key) a.hours[pend.month - 1] = pend.hours
    }
    store.planning = p
    store.planningYear = year
    store.remoteChange = null
  } catch (e) {
    store.loadError = e.message
    if (!silent) toastError(e)
  } finally {
    store.loading = false
  }
}

export const index = computed(() => {
  const p = store.planning
  if (!p) return null
  const byId = (list) => new Map(list.map((x) => [x.id, x]))
  const allocByResource = new Map()
  const allocByAssignment = new Map()
  for (const a of p.allocations) {
    if (!allocByResource.has(a.resourceId)) allocByResource.set(a.resourceId, [])
    allocByResource.get(a.resourceId).push(a)
    if (!allocByAssignment.has(a.assignmentId)) allocByAssignment.set(a.assignmentId, [])
    allocByAssignment.get(a.assignmentId).push(a)
  }
  return {
    teams: byId(p.teams),
    resources: byId(p.resources),
    assignments: byId(p.assignments),
    objects: byId(p.objects),
    allocByResource,
    allocByAssignment,
  }
})

export const thresholds = computed(() => store.planning?.settings ?? undefined)

/** Planerade timmar per månad för en resurs (live, inkl. osparade ändringar). */
export function resourceAllocated(resourceId) {
  const rows = index.value?.allocByResource.get(resourceId) ?? []
  const out = new Array(12).fill(0)
  for (const r of rows) for (let m = 0; m < 12; m++) out[m] += r.hours[m] || 0
  return out
}

/** Fördelat totalt (alla år) för ett uppdrag, live. */
export function assignmentAllocatedTotal(assignment) {
  const rows = index.value?.allocByAssignment.get(assignment.id) ?? []
  const liveYear = sum(rows.map((r) => sum(r.hours)))
  return assignment.allocatedTotal - assignment.allocatedYear + liveYear
}
export function assignmentYearByMonth(assignmentId) {
  const rows = index.value?.allocByAssignment.get(assignmentId) ?? []
  const out = new Array(12).fill(0)
  for (const r of rows) for (let m = 0; m < 12; m++) out[m] += r.hours[m] || 0
  return out
}
export const assignmentRemaining = (a) => a.estimatedHours - assignmentAllocatedTotal(a)

// ---------- Sparkö för celler (debounce 600 ms) ----------
const pending = new Map()
let saveTimer = null
let saving = null
const undoStack = []

export function setCell(allocation, monthIndex, hours, { record = true } = {}) {
  const old = allocation.hours[monthIndex]
  if (old === hours) return
  if (record) undoStack.push({ allocationId: allocation.id, monthIndex, old, hours })
  if (undoStack.length > 200) undoStack.shift()
  allocation.hours[monthIndex] = hours
  pending.set(`${allocation.id}:${monthIndex + 1}`, {
    allocationId: allocation.id,
    month: monthIndex + 1,
    hours,
    expected: allocation._server?.[monthIndex] ?? 0,
  })
  store.save.state = 'pending'
  clearTimeout(saveTimer)
  saveTimer = setTimeout(flushSaves, 600)
}

export function setRow(allocation, hours) {
  const before = [...allocation.hours]
  hours.forEach((h, m) => setCell(allocation, m, h, { record: false }))
  return before
}

export function undoLastCell() {
  const last = undoStack.pop()
  if (!last) return false
  const a = store.planning?.allocations.find((x) => x.id === last.allocationId)
  if (!a) return false
  setCell(a, last.monthIndex, last.old, { record: false })
  return true
}

export const hasPendingSaves = () => pending.size > 0 || !!saving

export async function flushSaves({ force = false, cells = null } = {}) {
  clearTimeout(saveTimer)
  if (saving) await saving
  if (!pending.size && !cells) return
  const batch = cells ?? [...pending.values()]
  if (!cells) pending.clear()
  const year = store.planningYear ?? store.year
  store.save.state = 'saving'
  saving = (async () => {
    try {
      const res = await api.put('/api/allocations/cells', { year, cells: batch, force })
      for (const u of res.updated) {
        const a = store.planning?.allocations.find((x) => x.id === u.id)
        if (!a) continue
        for (let m = 0; m < 12; m++) {
          a._server[m] = u.hours[m]
          if (!pending.has(`${a.id}:${m + 1}`)) a.hours[m] = u.hours[m]
        }
      }
      if (res.conflicts.length) handleConflicts(res.conflicts)
      store.save = { state: 'saved', at: new Date(), error: null }
    } catch (e) {
      // Lägg tillbaka i kön så att inget tappas bort.
      for (const c of batch) {
        const key = `${c.allocationId}:${c.month}`
        if (!pending.has(key)) pending.set(key, c)
      }
      store.save = { state: 'error', at: null, error: e.message }
      toast(`Kunde inte spara: ${e.message}`, {
        type: 'error',
        action: { label: 'Försök igen', fn: () => flushSaves() },
      })
    } finally {
      saving = null
    }
  })()
  await saving
}

function handleConflicts(conflicts) {
  const idx = index.value
  const names = conflicts
    .slice(0, 3)
    .map((c) => {
      const a = store.planning.allocations.find((x) => x.id === c.allocationId)
      const r = a && idx?.resources.get(a.resourceId)
      return `${r?.name ?? ''} ${['jan', 'feb', 'mar', 'apr', 'maj', 'jun', 'jul', 'aug', 'sep', 'okt', 'nov', 'dec'][c.month - 1]}`
    })
    .join(', ')
  const who = [...new Set(conflicts.map((c) => c.changedBy).filter(Boolean))].join(', ') || 'någon annan'
  // Servern vinner tills användaren väljer att skriva över.
  for (const c of conflicts) {
    const a = store.planning.allocations.find((x) => x.id === c.allocationId)
    if (a) {
      a.hours[c.month - 1] = c.current
      a._server[c.month - 1] = c.current
    }
  }
  toast(`${who} hann ändra samma cell (${names}). Deras värde visas nu.`, {
    type: 'error',
    action: {
      label: 'Använd mina värden',
      fn: () => {
        for (const c of conflicts) {
          const a = store.planning.allocations.find((x) => x.id === c.allocationId)
          if (a) a.hours[c.month - 1] = c.attempted
        }
        flushSaves({
          force: true,
          cells: conflicts.map((c) => ({ allocationId: c.allocationId, month: c.month, hours: c.attempted })),
        })
      },
    },
  })
}

// ---------- Strukturella ändringar ----------
export async function createAllocation(resourceId, assignmentId, hours = null) {
  const res = await api.post('/api/allocations', {
    resourceId,
    assignmentId,
    year: store.planningYear ?? store.year,
    hours,
  })
  await loadPlanning(store.planningYear ?? store.year, { silent: true })
  return res
}

export async function deleteAllocation(id) {
  await flushSaves()
  await api.del(`/api/allocations/${id}`)
  await loadPlanning(store.planningYear ?? store.year, { silent: true })
}

export async function moveAllocation(id, resourceId) {
  await flushSaves()
  const res = await api.put(`/api/allocations/${id}/move`, { resourceId })
  await loadPlanning(store.planningYear ?? store.year, { silent: true })
  return res
}

export async function reorderBacklog(ids) {
  const p = store.planning
  if (p) {
    const order = new Map(ids.map((id, i) => [id, i + 1]))
    let n = ids.length
    for (const a of [...p.assignments].sort((x, y) => x.priority - y.priority)) {
      a.priority = order.get(a.id) ?? ++n
    }
    p.assignments.sort((x, y) => x.priority - y.priority)
  }
  await api.put('/api/assignments/order', ids)
}

// ---------- Bevakning: nya ändringar och nya versioner ----------
let pollers = []
export function startPolling() {
  stopPolling()
  pollers.push(
    setInterval(async () => {
      if (document.hidden || !store.planning) return
      try {
        const latest = await api.get('/api/changes/latest')
        if (latest && latest.id > store.planning.lastChangeId) {
          const mine = latest.user === store.me?.displayName
          if (mine) {
            store.planning.lastChangeId = latest.id
          } else if (!hasPendingSaves() && !store.focus) {
            await loadPlanning(store.planningYear, { silent: true })
            toast(`Uppdaterat: ${latest.user} – ${latest.summary}`, { timeout: 5000 })
          } else {
            store.remoteChange = latest
          }
        }
      } catch {
        /* nätverksfel ignoreras vid bevakning */
      }
    }, 20000),
  )
  pollers.push(
    setInterval(async () => {
      if (document.hidden || !store.version) return
      try {
        const v = await api.get('/api/version')
        if (v.version !== store.version.version || v.startedAt !== store.version.startedAt) store.newVersion = v
      } catch {
        /* ignoreras */
      }
    }, 120000),
  )
}
export function stopPolling() {
  pollers.forEach(clearInterval)
  pollers = []
}

window.addEventListener('beforeunload', (e) => {
  if (pending.size) {
    flushSaves()
    e.preventDefault()
  }
})

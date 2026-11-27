<script setup>
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '../api.js'
import {
  store,
  index,
  canEdit,
  thresholds,
  loadPlanning,
  resourceAllocated,
  assignmentAllocatedTotal,
  assignmentRemaining,
  setRow,
  setCell,
  deleteAllocation,
  moveAllocation,
  toast,
  toastError,
} from '../store.js'
import { MONTHS, MONTH_NAMES, sum, sumMonths, distribute, fillRight, occLevel, pct } from '../utils/calc.js'
import { fmt, fmtPct } from '../utils/format.js'
import OccCell from '../components/OccCell.vue'
import Expander from '../components/Expander.vue'
import ResourceBlock from '../components/ResourceBlock.vue'
import BacklogPanel from '../components/BacklogPanel.vue'
import AllocationDialog from '../components/AllocationDialog.vue'
import AssignmentDialog from '../components/AssignmentDialog.vue'
import Modal from '../components/Modal.vue'
import Icon from '../components/Icon.vue'
import { confirmDialog } from '../components/dialogs.js'

const route = useRoute()
const router = useRouter()

// ---------- Visningsinställningar (sparas lokalt) ----------
const ls = {
  get(k, d) {
    try {
      const v = localStorage.getItem(k)
      return v === null ? d : JSON.parse(v)
    } catch {
      return d
    }
  },
  set(k, v) {
    try {
      localStorage.setItem(k, JSON.stringify(v))
    } catch {
      /* ignoreras */
    }
  },
}
const expanded = ref(new Set(ls.get('rk-expanded', [])))
const collapsedTeams = ref(new Set(ls.get('rk-collapsed-teams', [])))
const mode = ref(ls.get('rk-mode', 'hours'))
const showPanel = ref(ls.get('rk-panel', window.innerWidth >= 1200))
const onlyOver = ref(false)
const showInactive = ref(false)
const q = ref(String(route.query.q ?? ''))
const teamFilter = ref(new Set(String(route.query.team ?? '').split(',').filter(Boolean).map(Number)))

watch(expanded, (v) => ls.set('rk-expanded', [...v]), { deep: true })
watch(collapsedTeams, (v) => ls.set('rk-collapsed-teams', [...v]), { deep: true })
watch(mode, (v) => ls.set('rk-mode', v))
watch(showPanel, (v) => ls.set('rk-panel', v))
watch([q, teamFilter], () => {
  const query = { ...route.query, q: q.value || undefined, team: [...teamFilter.value].join(',') || undefined }
  router.replace({ query })
}, { deep: true })

const year = computed(() => store.planningYear ?? store.year)
const currentMonth = computed(() => (year.value === new Date().getFullYear() ? new Date().getMonth() : -1))

// ---------- Filtrering och gruppering ----------
const resources = computed(() => {
  const p = store.planning
  if (!p) return []
  const term = q.value.trim().toLowerCase()
  return p.resources.filter((r) => {
    if (!showInactive.value && !r.isActive) return false
    if (teamFilter.value.size && !teamFilter.value.has(r.teamId ?? 0)) return false
    if (term) {
      const hay = [r.name, r.role, ...(index.value.allocByResource.get(r.id) ?? []).map((a) => index.value.assignments.get(a.assignmentId)?.name)]
        .join(' ')
        .toLowerCase()
      if (!hay.includes(term)) return false
    }
    if (onlyOver.value) {
      const alloc = resourceAllocated(r.id)
      if (!r.capacity.some((c, m) => ['near', 'over'].includes(occLevel(alloc[m], c, thresholds.value)))) return false
    }
    return true
  })
})

const groups = computed(() => {
  const p = store.planning
  if (!p) return []
  const list = p.teams.map((t) => ({ id: t.id, team: t, resources: [] }))
  const noTeam = { id: 0, team: { id: 0, name: 'Utan team', color: 'var(--text-faint)' }, resources: [] }
  const byId = new Map(list.map((g) => [g.id, g]))
  for (const r of resources.value) (byId.get(r.teamId) ?? noTeam).resources.push(r)
  return [...list, noTeam]
    .filter((g) => g.resources.length)
    .map((g) => ({
      ...g,
      capacity: sumMonths(g.resources.map((r) => r.capacity)),
      allocated: sumMonths(g.resources.map((r) => resourceAllocated(r.id))),
    }))
})

const totals = computed(() => ({
  capacity: sumMonths(groups.value.map((g) => g.capacity)),
  allocated: sumMonths(groups.value.map((g) => g.allocated)),
}))

function toggleResource(id) {
  const s = new Set(expanded.value)
  s.has(id) ? s.delete(id) : s.add(id)
  expanded.value = s
}
function toggleTeam(id) {
  const s = new Set(collapsedTeams.value)
  s.has(id) ? s.delete(id) : s.add(id)
  collapsedTeams.value = s
}
function expandAll(open) {
  expanded.value = open ? new Set(resources.value.map((r) => r.id)) : new Set()
  if (open) collapsedTeams.value = new Set()
}
function toggleTeamFilter(id) {
  const s = new Set(teamFilter.value)
  s.has(id) ? s.delete(id) : s.add(id)
  teamFilter.value = s
}

// ---------- Scroll / sticky skugga ----------
const wrap = ref(null)
const scrolled = ref(false)
const onScroll = () => (scrolled.value = (wrap.value?.scrollLeft ?? 0) > 0)
onMounted(async () => {
  await nextTick()
  if (wrap.value && currentMonth.value > 2 && wrap.value.clientWidth < wrap.value.scrollWidth) {
    wrap.value.scrollLeft = (currentMonth.value - 1) * 52
  }
})

// ---------- Aktiv rad: "kvar att fördela" ----------
const active = ref(null) // { allocation, month }
const activeAlloc = computed(() =>
  active.value ? store.planning?.allocations.find((a) => a.id === active.value.allocation.id) : null,
)
const activeAsg = computed(() => (activeAlloc.value ? index.value?.assignments.get(activeAlloc.value.assignmentId) : null))
const activeRes = computed(() => (activeAlloc.value ? index.value?.resources.get(activeAlloc.value.resourceId) : null))
const activeStats = computed(() => {
  const a = activeAsg.value
  if (!a) return null
  const allocated = assignmentAllocatedTotal(a)
  const remaining = a.estimatedHours - allocated
  const m = active.value.month
  const resAlloc = resourceAllocated(activeRes.value.id)
  return {
    allocated,
    remaining,
    progress: a.estimatedHours ? Math.min(100, (allocated / a.estimatedHours) * 100) : allocated ? 100 : 0,
    state: remaining < 0 ? 'over' : remaining === 0 ? 'done' : '',
    month: m,
    capacity: activeRes.value.capacity[m],
    planned: resAlloc[m],
    free: activeRes.value.capacity[m] - resAlloc[m],
    rowSum: sum(activeAlloc.value.hours),
  }
})
const onFocusCell = (e) => (active.value = e)

// ---------- Radåtgärder ----------
const menu = reactive({ open: false, x: 0, y: 0, allocation: null })
function openRowMenu({ allocation, anchor }) {
  const r = anchor.getBoundingClientRect()
  const w = 230
  menu.x = Math.min(window.innerWidth - w - 8, r.left)
  menu.y = r.bottom + 4 + 280 > window.innerHeight ? Math.max(8, r.top - 284) : r.bottom + 4
  menu.allocation = allocation
  menu.open = true
  active.value = { allocation, month: active.value?.allocation.id === allocation.id ? active.value.month : Math.max(0, currentMonth.value) }
}
const closeMenu = () => (menu.open = false)

function doFillRight(a = activeAlloc.value, m = active.value?.month ?? 0) {
  closeMenu()
  if (!a) return
  const next = fillRight(a.hours, m)
  setRow(a, next)
  toast(`Fyllde ${MONTHS[m]} → DEC med ${a.hours[m] || 0} h.`, { timeout: 2500 })
}

async function doClearRow(a = activeAlloc.value) {
  closeMenu()
  if (!a) return
  const before = setRow(a, new Array(12).fill(0))
  toast('Raden nollställdes.', {
    action: { label: 'Ångra', fn: () => before.forEach((h, m) => setCell(a, m, h, { record: false })) },
  })
}

async function doDeleteRow(a = menu.allocation) {
  closeMenu()
  const asg = index.value.assignments.get(a.assignmentId)
  const res = index.value.resources.get(a.resourceId)
  const ok = await confirmDialog({
    title: 'Ta bort uppdragsrad?',
    message: `${asg?.name} tas bort från ${res?.name}. Alla fördelade timmar på raden (alla år) tas bort.`,
    confirmLabel: 'Ta bort',
    danger: true,
  })
  if (!ok) return
  try {
    await deleteAllocation(a.id)
    if (active.value?.allocation.id === a.id) active.value = null
    toast('Raden togs bort.')
  } catch (e) {
    toastError(e)
  }
}

// Fördela jämnt
const dist = reactive({ open: false, allocation: null, total: 0, from: 0, to: 11, mode: 'capacity', replace: true })
function openDistribute(a = activeAlloc.value) {
  closeMenu()
  if (!a) return
  const asg = index.value.assignments.get(a.assignmentId)
  const remaining = assignmentRemaining(asg)
  Object.assign(dist, {
    open: true,
    allocation: a,
    total: sum(a.hours) + Math.max(0, remaining),
    from: Math.max(0, currentMonth.value),
    to: 11,
    mode: 'capacity',
  })
}
const distPreview = computed(() => {
  if (!dist.allocation) return []
  const months = []
  for (let m = Math.min(dist.from, dist.to); m <= Math.max(dist.from, dist.to); m++) months.push(m)
  const res = index.value.resources.get(dist.allocation.resourceId)
  const keep = dist.allocation.hours.map((h, m) => (months.includes(m) ? 0 : h))
  const fixed = sum(keep)
  const weights =
    dist.mode === 'capacity'
      ? res.capacity
      : dist.mode === 'free'
        ? res.capacity.map((c, m) => Math.max(0, c - resourceAllocated(res.id)[m] + dist.allocation.hours[m]))
        : null
  const spread = distribute(Math.max(0, dist.total - fixed), months, weights)
  return keep.map((h, m) => h + spread[m])
})
function applyDistribute() {
  setRow(dist.allocation, distPreview.value)
  dist.open = false
  toast('Timmarna fördelades.', { type: 'success', timeout: 2000 })
}

// Flytta rad
const mover = reactive({ open: false, allocation: null, target: null })
function openMove(a = menu.allocation) {
  closeMenu()
  Object.assign(mover, { open: true, allocation: a, target: null })
}
async function applyMove() {
  try {
    const res = await moveAllocation(mover.allocation.id, mover.target)
    expanded.value = new Set([...expanded.value, mover.target])
    flash(res?.id)
    mover.open = false
    toast('Raden flyttades.', { type: 'success' })
  } catch (e) {
    toastError(e)
  }
}

// Kommentar
const note = reactive({ open: false, allocation: null, text: '' })
function openNote(a = menu.allocation) {
  closeMenu()
  Object.assign(note, { open: true, allocation: a, text: a.note ?? '' })
}
async function saveNote() {
  try {
    await api.put(`/api/allocations/${note.allocation.id}/note`, { note: note.text })
    note.allocation.note = note.text.trim() || null
    note.open = false
  } catch (e) {
    toastError(e)
  }
}

// Frånvaro / avvikande kapacitet
const absence = reactive({ open: false, resource: null, month: 0, hours: 0, reason: 'Semester', existing: false })
function openAbsence(resource, month) {
  if (!canEdit.value) return
  const ex = resource.absences.find((a) => a.month === month + 1)
  Object.assign(absence, {
    open: true,
    resource,
    month,
    hours: ex ? ex.hours : 0,
    reason: ex?.reason ?? 'Semester',
    existing: !!ex,
  })
}
async function saveAbsence(reset = false) {
  try {
    await api.put(`/api/resources/${absence.resource.id}/absences`, {
      year: year.value,
      month: absence.month + 1,
      hours: reset ? null : Number(absence.hours) || 0,
      reason: absence.reason,
    })
    absence.open = false
    await loadPlanning(year.value, { silent: true })
    toast(reset ? 'Kapaciteten återställdes.' : 'Kapaciteten uppdaterades.', { type: 'success', timeout: 2000 })
  } catch (e) {
    toastError(e)
  }
}

// Tilldela (drop eller knapp)
const assignDlg = reactive({ open: false, assignmentId: null, resourceId: null })
function openAssign({ assignmentId = null, resourceId = null } = {}) {
  Object.assign(assignDlg, { open: true, assignmentId, resourceId })
}
const flashAllocId = ref(null)
function flash(id) {
  flashAllocId.value = id
  setTimeout(() => (flashAllocId.value = null), 1200)
}
function onAssigned({ resourceId, assignmentId }) {
  expanded.value = new Set([...expanded.value, resourceId])
  const a = store.planning.allocations.find((x) => x.resourceId === resourceId && x.assignmentId === assignmentId)
  if (a) {
    flash(a.id)
    active.value = { allocation: a, month: Math.max(0, currentMonth.value) }
  }
}

const asgDlg = reactive({ open: false, assignment: null })
const openAssignment = (a) => Object.assign(asgDlg, { open: true, assignment: a ?? null })

const exportUrl = computed(() => `/api/export/planning.csv?year=${year.value}`)
</script>

<template>
  <div class="planning">
    <div class="toolbar">
      <h1 class="title">Planering <span class="muted">{{ year }}</span></h1>
      <div class="search">
        <Icon name="search" :size="14" />
        <input v-model="q" class="input" type="search" placeholder="Sök resurs, roll eller uppdrag…" aria-label="Sök" />
      </div>
      <div class="chips" role="group" aria-label="Filtrera på team">
        <button
          v-for="t in store.planning?.teams ?? []"
          :key="t.id"
          class="chip dot"
          :style="{ '--chip-color': t.color }"
          :aria-pressed="teamFilter.has(t.id)"
          @click="toggleTeamFilter(t.id)"
        >
          {{ t.name }}
        </button>
        <button class="chip" :aria-pressed="onlyOver" @click="onlyOver = !onlyOver">▲ Överbelagda</button>
        <button class="chip" :aria-pressed="showInactive" @click="showInactive = !showInactive">Visa inaktiva</button>
      </div>
      <span class="spacer" />
      <div class="segmented" role="group" aria-label="Visa som">
        <button :aria-pressed="mode === 'hours'" @click="mode = 'hours'">Timmar</button>
        <button :aria-pressed="mode === 'percent'" @click="mode = 'percent'">%</button>
        <button :aria-pressed="mode === 'free'" @click="mode = 'free'">Ledigt</button>
      </div>
      <button class="btn sm" title="Expandera alla" @click="expandAll(true)"><Icon name="expand" :size="14" /><span class="hide-md">Expandera</span></button>
      <button class="btn sm" title="Fäll ihop alla" @click="expandAll(false)"><Icon name="collapse" :size="14" /><span class="hide-md">Fäll ihop</span></button>
      <a class="btn sm" :href="exportUrl" title="Exportera till Excel (CSV)"><Icon name="download" :size="14" /><span class="hide-md">Excel</span></a>
      <button class="btn sm" :class="{ primary: showPanel }" :aria-pressed="showPanel" @click="showPanel = !showPanel">
        <Icon name="inbox" :size="14" /> Backlogg
      </button>
    </div>

    <Transition name="fade">
      <div v-if="store.remoteChange" class="remote" role="status">
        <Icon name="refresh" :size="14" />
        <span><strong>{{ store.remoteChange.user }}</strong> har ändrat: {{ store.remoteChange.summary }}</span>
        <button class="btn sm" @click="loadPlanning(year, { silent: true })">Uppdatera</button>
      </div>
    </Transition>

    <div class="layout" :class="{ 'with-panel': showPanel }">
      <div ref="wrap" class="grid-wrap" :class="{ scrolled }" @scroll.passive="onScroll">
        <div v-if="!store.planning && store.loading" class="skeleton-grid">
          <div v-for="i in 8" :key="i" class="skeleton" :style="{ height: '40px', marginBottom: '6px' }" />
        </div>
        <div v-else-if="store.planning" class="grid" :class="{ loading: store.loading }">
          <div class="g-row g-head">
            <div class="c-name">Resurs</div>
            <div v-for="(m, i) in MONTHS" :key="m" class="mh" :class="{ current: i === currentMonth }">{{ m }}</div>
            <div class="mh c-year">År</div>
          </div>

          <template v-for="g in groups" :key="g.id">
            <div class="g-row team-row">
              <div class="c-name">
                <button class="team-toggle" :aria-expanded="!collapsedTeams.has(g.id)" @click="toggleTeam(g.id)">
                  <Icon name="chevron-right" :size="16" class="chev" />
                  <span class="team-swatch" :style="{ background: g.team.color }" />
                  <span class="ellipsis">{{ g.team.name }}</span>
                  <span class="chip">{{ g.resources.length }}</span>
                </button>
              </div>
              <div v-for="(c, m) in g.capacity" :key="m" class="c-occ">
                <OccCell :allocated="g.allocated[m]" :capacity="c" :month="m" :label="g.team.name" :mode="mode" :thresholds="thresholds" />
              </div>
              <div class="c-year">
                <OccCell :allocated="sum(g.allocated)" :capacity="sum(g.capacity)" :label="`${g.team.name}, helår`" :mode="mode" :thresholds="thresholds" />
              </div>
            </div>
            <Expander :open="!collapsedTeams.has(g.id)">
              <ResourceBlock
                v-for="r in g.resources"
                :key="r.id"
                :resource="r"
                :open="expanded.has(r.id)"
                :mode="mode"
                :editable="canEdit"
                :current-month="currentMonth"
                :flash-alloc-id="flashAllocId"
                :active-alloc-id="activeAlloc?.id ?? null"
                @toggle="toggleResource(r.id)"
                @drop="(id) => openAssign({ assignmentId: id, resourceId: r.id })"
                @add="openAssign({ resourceId: r.id })"
                @absence="(m) => openAbsence(r, m)"
                @row-menu="openRowMenu"
                @focus-cell="onFocusCell"
                @edit-assignment="openAssignment"
              />
            </Expander>
          </template>

          <div v-if="!groups.length" class="empty">
            <Icon name="search" :size="36" class="icon" />
            <h3>Inga resurser att visa</h3>
            <p>{{ store.planning.resources.length ? 'Inga träffar med nuvarande filter.' : 'Lägg till resurser under Admin, eller importera från Excel.' }}</p>
            <button v-if="q || teamFilter.size || onlyOver" class="btn" @click="((q = ''), (teamFilter = new Set()), (onlyOver = false))">Rensa filter</button>
            <RouterLink v-else-if="canEdit" class="btn primary" to="/admin/resurser">Gå till Admin</RouterLink>
          </div>

          <div v-if="groups.length" class="g-row total-row">
            <div class="c-name">Totalt</div>
            <div v-for="(c, m) in totals.capacity" :key="m" class="c-occ">
              <OccCell :allocated="totals.allocated[m]" :capacity="c" :month="m" label="Alla" :mode="mode" :thresholds="thresholds" />
            </div>
            <div class="c-year">
              <OccCell :allocated="sum(totals.allocated)" :capacity="sum(totals.capacity)" label="Alla, helår" :mode="mode" :thresholds="thresholds" />
            </div>
          </div>
        </div>
      </div>

      <Transition name="panel">
        <aside v-if="showPanel" class="panel" aria-label="Backloggpanel">
          <button class="btn ghost icon sm panel-close" aria-label="Stäng backlogg" @click="showPanel = false"><Icon name="x" :size="16" /></button>
          <BacklogPanel
            compact
            @assign="(a) => openAssign({ assignmentId: a.id })"
            @edit="openAssignment"
            @create="openAssignment(null)"
          />
        </aside>
      </Transition>
    </div>

    <!-- Fördelningspanel: kvar att fördela för aktiv rad -->
    <Transition name="rise">
      <div v-if="activeStats && canEdit" class="distbar" role="region" aria-label="Fördelning för vald rad">
        <div class="db-main">
          <div class="db-title">
            <strong class="ellipsis">{{ activeAsg.name }}</strong>
            <span class="muted small ellipsis">{{ activeRes.name }} · {{ MONTH_NAMES[activeStats.month] }}</span>
          </div>
          <div class="db-progress">
            <div class="progress" :class="activeStats.state">
              <div class="fill" :style="{ width: activeStats.progress + '%' }" />
            </div>
            <span class="small muted num">{{ fmt(activeStats.allocated) }} / {{ fmt(activeAsg.estimatedHours) }} h</span>
          </div>
          <div class="db-kvar" :class="activeStats.state">
            <span class="xs muted">Kvar att fördela</span>
            <strong class="num">
              <template v-if="activeStats.remaining === 0">✓ 0 h</template>
              <template v-else-if="activeStats.remaining > 0">{{ fmt(activeStats.remaining) }} h</template>
              <template v-else>{{ fmt(-activeStats.remaining) }} h över</template>
            </strong>
          </div>
          <div class="db-month">
            <span class="xs muted">{{ activeRes.name.split(' ')[0] }} i {{ MONTHS[activeStats.month] }}</span>
            <strong class="num" :class="activeStats.free < 0 ? 'danger-text' : ''">
              {{ activeStats.free >= 0 ? `${fmt(activeStats.free)} h ledigt` : `${fmt(-activeStats.free)} h över` }}
            </strong>
            <span class="xs faint num">{{ fmt(activeStats.planned) }} / {{ fmt(activeStats.capacity) }} h ({{ fmtPct(pct(activeStats.planned, activeStats.capacity)) }})</span>
          </div>
        </div>
        <div class="db-actions">
          <button class="btn sm" title="Kopiera vald månad till resten av året (Ctrl+Enter)" @click="doFillRight()"><Icon name="fill-right" :size="14" /> Fyll höger</button>
          <button class="btn sm" @click="openDistribute()"><Icon name="equal" :size="14" /> Fördela…</button>
          <button class="btn sm" @click="doClearRow()"><Icon name="eraser" :size="14" /> Nollställ</button>
          <button class="btn ghost icon sm" aria-label="Stäng" @click="active = null"><Icon name="x" :size="14" /></button>
        </div>
      </div>
    </Transition>

    <!-- Radmeny -->
    <Teleport to="body">
      <div v-if="menu.open" class="menu-backdrop" @click="closeMenu" @keydown.esc="closeMenu">
        <div class="row-menu-pop" :style="{ left: menu.x + 'px', top: menu.y + 'px' }" role="menu" @click.stop>
          <button role="menuitem" class="mi" @click="doFillRight(menu.allocation, active?.month ?? 0)"><Icon name="fill-right" :size="16" /> Fyll höger från {{ MONTHS[active?.month ?? 0] }}</button>
          <button role="menuitem" class="mi" @click="openDistribute(menu.allocation)"><Icon name="equal" :size="16" /> Fördela timmar…</button>
          <button role="menuitem" class="mi" @click="doClearRow(menu.allocation)"><Icon name="eraser" :size="16" /> Nollställ rad</button>
          <button role="menuitem" class="mi" @click="openMove()"><Icon name="move" :size="16" /> Flytta till resurs…</button>
          <button role="menuitem" class="mi" @click="openNote()"><Icon name="edit" :size="16" /> Kommentar…</button>
          <hr />
          <button role="menuitem" class="mi danger-text" @click="doDeleteRow()"><Icon name="trash" :size="16" /> Ta bort rad</button>
        </div>
      </div>
    </Teleport>

    <AllocationDialog
      v-if="assignDlg.open"
      :assignment-id="assignDlg.assignmentId"
      :resource-id="assignDlg.resourceId"
      @close="assignDlg.open = false"
      @done="onAssigned"
    />
    <AssignmentDialog v-if="asgDlg.open" :assignment="asgDlg.assignment" @close="asgDlg.open = false" />

    <Modal v-if="dist.open" title="Fördela timmar" :subtitle="`${index.assignments.get(dist.allocation.assignmentId)?.name} · ${index.resources.get(dist.allocation.resourceId)?.name}`" width="720px" @close="dist.open = false">
      <div class="stack">
        <div class="controls">
          <label class="field" style="width: 150px"><span>Totalt på raden ({{ year }})</span><input v-model.number="dist.total" class="input num" type="number" min="0" /></label>
          <label class="field"><span>Från</span><select v-model.number="dist.from" class="input"><option v-for="(m, i) in MONTHS" :key="m" :value="i">{{ m }}</option></select></label>
          <label class="field"><span>Till</span><select v-model.number="dist.to" class="input"><option v-for="(m, i) in MONTHS" :key="m" :value="i">{{ m }}</option></select></label>
          <div class="field">
            <span>Viktning</span>
            <div class="segmented">
              <button :aria-pressed="dist.mode === 'capacity'" @click="dist.mode = 'capacity'">Efter kapacitet</button>
              <button :aria-pressed="dist.mode === 'free'" @click="dist.mode = 'free'">Efter ledig tid</button>
              <button :aria-pressed="dist.mode === 'even'" @click="dist.mode = 'even'">Jämnt</button>
            </div>
          </div>
        </div>
        <p class="muted small">Månader utanför perioden behålls. Kvar att fördela på uppdraget: <strong>{{ fmt(assignmentRemaining(index.assignments.get(dist.allocation.assignmentId))) }} h</strong></p>
        <div class="preview-table">
          <div v-for="(m, i) in MONTHS" :key="m" class="pv">
            <span class="xs muted">{{ m }}</span>
            <span class="num faint xs">{{ dist.allocation.hours[i] || '–' }}</span>
            <strong class="num" :class="{ changed: distPreview[i] !== dist.allocation.hours[i] }">{{ distPreview[i] || '–' }}</strong>
          </div>
        </div>
        <p class="small">Summa: <strong class="num">{{ fmt(sum(distPreview)) }} h</strong> (tidigare {{ fmt(sum(dist.allocation.hours)) }} h)</p>
      </div>
      <template #footer>
        <button class="btn" @click="dist.open = false">Avbryt</button>
        <button class="btn primary" @click="applyDistribute">Verkställ</button>
      </template>
    </Modal>

    <Modal v-if="mover.open" title="Flytta till resurs" :subtitle="index.assignments.get(mover.allocation.assignmentId)?.name" width="440px" @close="mover.open = false">
      <label class="field">
        <span>Ny resurs</span>
        <select v-model="mover.target" class="input">
          <option :value="null" disabled>Välj resurs…</option>
          <option v-for="r in store.planning.resources.filter((x) => x.id !== mover.allocation.resourceId && x.isActive)" :key="r.id" :value="r.id">
            {{ r.name }} – {{ fmt(sum(r.capacity) - sum(resourceAllocated(r.id))) }} h ledigt {{ year }}
          </option>
        </select>
      </label>
      <p class="muted small" style="margin-top: 8px">Alla timmar på raden (alla år) flyttas. Har resursen redan uppdraget slås raderna ihop.</p>
      <template #footer>
        <button class="btn" @click="mover.open = false">Avbryt</button>
        <button class="btn primary" :disabled="!mover.target" @click="applyMove">Flytta</button>
      </template>
    </Modal>

    <Modal v-if="note.open" title="Kommentar" width="480px" @close="note.open = false">
      <textarea v-model="note.text" class="input" rows="4" placeholder="T.ex. 'Halvtid under införandet'" />
      <template #footer>
        <button class="btn" @click="note.open = false">Avbryt</button>
        <button class="btn primary" @click="saveNote">Spara</button>
      </template>
    </Modal>

    <Modal v-if="absence.open" title="Kapacitet / frånvaro" :subtitle="`${absence.resource.name} · ${MONTH_NAMES[absence.month]} ${year}`" width="440px" @close="absence.open = false">
      <div class="stack">
        <p class="muted small">Standardkapaciteten räknas fram från inställningar och sysselsättningsgrad. Här kan du ange avvikande tillgänglig tid för månaden, t.ex. vid semester eller utbildning.</p>
        <div class="form-grid">
          <label class="field"><span>Tillgänglig tid (h)</span><input v-model.number="absence.hours" class="input num" type="number" min="0" max="744" /></label>
          <label class="field">
            <span>Orsak</span>
            <input v-model="absence.reason" class="input" list="reasons" />
            <datalist id="reasons">
              <option>Semester</option><option>Föräldraledighet</option><option>Tjänstledighet</option><option>Utbildning</option><option>Sjukdom</option><option>Annat uppdrag</option>
            </datalist>
          </label>
        </div>
      </div>
      <template #footer>
        <button v-if="absence.existing" class="btn" @click="saveAbsence(true)">Återställ standard</button>
        <span class="spacer" />
        <button class="btn" @click="absence.open = false">Avbryt</button>
        <button class="btn primary" @click="saveAbsence(false)">Spara</button>
      </template>
    </Modal>
  </div>
</template>

<style scoped>
.planning {
  display: flex;
  flex-direction: column;
  height: calc(100dvh - var(--topbar-h) - var(--bottomnav-h));
  padding: var(--sp-3) var(--sp-4) var(--sp-3);
  gap: var(--sp-2);
}
.toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--sp-2);
}
.title {
  font-size: var(--fs-xl);
  margin-right: var(--sp-2);
}
.search {
  position: relative;
  display: flex;
  align-items: center;
  width: 240px;
}
.search > .icon {
  position: absolute;
  left: 10px;
  color: var(--text-faint);
  pointer-events: none;
}
.search .input {
  height: 30px;
  padding-left: 30px;
}
.chips {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}
.chips .chip {
  height: 26px;
}
.remote {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  padding: 6px 12px;
  border-radius: var(--r-md);
  background: var(--accent-soft);
  font-size: var(--fs-sm);
}
.layout {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: var(--sp-3);
}
.layout.with-panel {
  grid-template-columns: minmax(0, 1fr) 330px;
}
.grid-wrap {
  min-height: 0;
}
.grid.loading {
  opacity: 0.7;
  transition: opacity var(--dur-2);
}
.skeleton-grid {
  padding: var(--sp-3);
}
.panel {
  position: relative;
  min-height: 0;
  overflow-x: hidden;
  overflow-y: auto;
  padding: var(--sp-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--r-lg);
  box-shadow: var(--sh-1);
}
.panel-close {
  display: none;
  position: absolute;
  right: 8px;
  top: 8px;
}

.distbar {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  flex-wrap: wrap;
  padding: 8px 12px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--r-lg);
  box-shadow: var(--sh-2);
}
.db-main {
  flex: 1;
  display: flex;
  align-items: center;
  gap: var(--sp-5);
  min-width: 0;
  flex-wrap: wrap;
}
.db-title {
  display: flex;
  flex-direction: column;
  min-width: 140px;
  max-width: 260px;
}
.db-progress {
  display: flex;
  flex-direction: column;
  gap: 4px;
  width: 200px;
}
.db-kvar,
.db-month {
  display: flex;
  flex-direction: column;
  line-height: 1.2;
}
.db-kvar strong {
  font-size: var(--fs-xl);
  color: var(--occ-near-fg);
  text-align: left;
}
.db-kvar.done strong {
  color: var(--occ-ok-fg);
}
.db-kvar.over strong {
  color: var(--occ-over-fg);
}
.db-month strong {
  text-align: left;
}
.db-actions {
  display: flex;
  gap: 6px;
  align-items: center;
}

.menu-backdrop {
  position: fixed;
  inset: 0;
  z-index: 90;
}
.row-menu-pop {
  position: fixed;
  width: 230px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  box-shadow: var(--sh-3);
  padding: 4px;
  animation: pop var(--dur-2) var(--ease-out);
}
.row-menu-pop hr {
  border: 0;
  border-top: 1px solid var(--border);
  margin: 4px 0;
}
.mi {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 8px 10px;
  border: 0;
  background: transparent;
  border-radius: var(--r-sm);
  font: inherit;
  color: var(--text);
  cursor: pointer;
  text-align: left;
}
.mi:hover,
.mi:focus-visible {
  background: var(--surface-2);
}
@keyframes pop {
  from {
    opacity: 0;
    transform: translateY(-4px);
  }
}

.controls {
  display: flex;
  flex-wrap: wrap;
  gap: var(--sp-3);
  align-items: flex-end;
}
.controls select {
  width: 90px;
}
.preview-table {
  display: grid;
  grid-template-columns: repeat(12, minmax(44px, 1fr));
  gap: 4px;
  overflow-x: auto;
}
.pv {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  padding: 6px;
  border-radius: var(--r-sm);
  background: var(--surface-2);
}
.pv .changed {
  color: var(--accent);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity var(--dur-2);
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
.rise-enter-active,
.rise-leave-active {
  transition:
    opacity var(--dur-3) var(--ease-out),
    transform var(--dur-3) var(--ease-out);
}
.rise-enter-from,
.rise-leave-to {
  opacity: 0;
  transform: translateY(12px);
}
.panel-enter-active,
.panel-leave-active {
  transition:
    opacity var(--dur-3) var(--ease-out),
    transform var(--dur-3) var(--ease-out);
}
.panel-enter-from,
.panel-leave-to {
  opacity: 0;
  transform: translateX(24px);
}

@media (max-width: 1199px) {
  .layout.with-panel {
    grid-template-columns: minmax(0, 1fr);
  }
  .panel {
    position: fixed;
    top: var(--topbar-h);
    right: 0;
    bottom: var(--bottomnav-h);
    width: min(360px, 92vw);
    z-index: 40;
    border-radius: 0;
    box-shadow: var(--sh-3);
    padding-top: var(--sp-5);
  }
  .panel-close {
    display: inline-flex;
  }
  .hide-md {
    display: none;
  }
}
@media (max-width: 767px) {
  .planning {
    padding: var(--sp-2);
  }
  .title {
    font-size: var(--fs-lg);
  }
  .search {
    width: 100%;
    order: 10;
  }
  .db-progress {
    width: 120px;
  }
  .db-main {
    gap: var(--sp-3);
  }
}
</style>

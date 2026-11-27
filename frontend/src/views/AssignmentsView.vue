<script setup>
// Uppdragssammanställning: per förvaltningsobjekt/projekt med estimat, fördelat, kvar och timmar per månad.
import { computed, reactive, ref } from 'vue'
import { store, index, canEdit, assignmentAllocatedTotal, assignmentYearByMonth } from '../store.js'
import { MONTHS, sum, sumMonths } from '../utils/calc.js'
import { fmt, STATUS, STATUS_ORDER } from '../utils/format.js'
import Expander from '../components/Expander.vue'
import Icon from '../components/Icon.vue'
import AssignmentDialog from '../components/AssignmentDialog.vue'

const year = computed(() => store.planningYear ?? store.year)
const currentMonth = computed(() => (year.value === new Date().getFullYear() ? new Date().getMonth() : -1))
const statusFilter = ref(new Set(['Backlogg', 'Planerad', 'Pagaende', 'Pausad']))
const q = ref('')
const openObjects = ref(new Set(['all']))
const openAsg = ref(new Set())
const asgDlg = reactive({ open: false, assignment: null })

function toggle(setRef, id) {
  const s = new Set(setRef.value)
  s.has(id) ? s.delete(id) : s.add(id)
  setRef.value = s
}

const rows = computed(() => {
  const p = store.planning
  if (!p) return []
  const term = q.value.trim().toLowerCase()
  return p.assignments
    .filter((a) => statusFilter.value.has(a.status))
    .filter((a) => !term || `${a.name} ${a.requester ?? ''} ${a.responsible ?? ''}`.toLowerCase().includes(term))
    .map((a) => {
      const allocated = assignmentAllocatedTotal(a)
      const months = assignmentYearByMonth(a.id)
      const people = (index.value.allocByAssignment.get(a.id) ?? []).map((al) => ({
        allocation: al,
        resource: index.value.resources.get(al.resourceId),
      }))
      return { a, allocated, remaining: a.estimatedHours - allocated, months, people }
    })
})

const groups = computed(() => {
  const p = store.planning
  if (!p) return []
  const list = [...p.objects, { id: 0, name: 'Utan objekt', type: null, color: 'var(--text-faint)' }]
  return list
    .map((o) => {
      const items = rows.value.filter((r) => (r.a.objectId ?? 0) === o.id)
      return {
        o,
        items,
        estimate: sum(items.map((i) => i.a.estimatedHours)),
        allocated: sum(items.map((i) => i.allocated)),
        months: sumMonths(items.map((i) => i.months)),
      }
    })
    .filter((g) => g.items.length)
})
const isOpen = (id) => openObjects.value.has('all') || openObjects.value.has(id)
function toggleObject(id) {
  const s = new Set(openObjects.value)
  if (s.has('all')) {
    s.delete('all')
    groups.value.forEach((g) => g.o.id !== id && s.add(g.o.id))
  } else s.has(id) ? s.delete(id) : s.add(id)
  openObjects.value = s
}
const progress = (r) => (r.a.estimatedHours ? Math.min(100, (r.allocated / r.a.estimatedHours) * 100) : r.allocated ? 100 : 0)
const state = (rem) => (rem < 0 ? 'over' : rem === 0 ? 'done' : '')
const kvarClass = (rem) => (rem < 0 ? 'neg' : rem === 0 ? 'zero' : 'pos')
function toggleStatus(s) {
  const n = new Set(statusFilter.value)
  n.has(s) ? n.delete(s) : n.add(s)
  statusFilter.value = n
}
</script>

<template>
  <div class="page wide">
    <div class="page-head">
      <div>
        <h1>Uppdrag {{ year }}</h1>
        <p class="sub">Sammanställning per förvaltningsobjekt och projekt – estimat, fördelat och kvar att fördela.</p>
      </div>
      <span class="spacer" />
      <a class="btn" :href="`/api/export/assignments.csv?year=${year}`"><Icon name="download" :size="16" /> Excel</a>
      <button v-if="canEdit" class="btn primary" @click="Object.assign(asgDlg, { open: true, assignment: null })"><Icon name="plus" :size="16" /> Nytt uppdrag</button>
    </div>

    <div class="filters">
      <div class="search">
        <Icon name="search" :size="14" />
        <input v-model="q" class="input" type="search" placeholder="Sök uppdrag, beställare…" aria-label="Sök uppdrag" />
      </div>
      <div class="row wrap" role="group" aria-label="Filtrera på status">
        <button v-for="s in STATUS_ORDER" :key="s" class="chip" :aria-pressed="statusFilter.has(s)" @click="toggleStatus(s)">{{ STATUS[s] }}</button>
      </div>
    </div>

    <div class="grid-wrap">
      <div class="grid asg-grid">
        <div class="g-row g-head">
          <div class="c-name">Uppdrag</div>
          <div class="mh">Estimat</div>
          <div class="mh">Fördelat</div>
          <div class="mh">Kvar</div>
          <div v-for="(m, i) in MONTHS" :key="m" class="mh" :class="{ current: i === currentMonth }">{{ m }}</div>
        </div>

        <template v-for="g in groups" :key="g.o.id">
          <div class="g-row team-row">
            <div class="c-name">
              <button class="team-toggle" :aria-expanded="isOpen(g.o.id)" @click="toggleObject(g.o.id)">
                <Icon name="chevron-right" :size="16" class="chev" />
                <span class="team-swatch" :style="{ background: g.o.color }" />
                <span class="ellipsis">{{ g.o.name }}</span>
                <span v-if="g.o.type === 'Projekt'" class="chip">Projekt</span>
                <span class="chip">{{ g.items.length }}</span>
              </button>
            </div>
            <div class="num v strong">{{ fmt(g.estimate) }}</div>
            <div class="num v strong">{{ fmt(g.allocated) }}</div>
            <div class="num v strong" :class="{ 'danger-text': g.estimate - g.allocated < 0 }">{{ fmt(g.estimate - g.allocated) }}</div>
            <div v-for="(h, m) in g.months" :key="m" class="num v strong" :class="{ cur: m === currentMonth }">{{ h ? fmt(h) : '' }}</div>
          </div>
          <Expander :open="isOpen(g.o.id)">
            <template v-for="r in g.items" :key="r.a.id">
              <div class="g-row a-row">
                <div class="c-name">
                  <button class="exp" :aria-expanded="openAsg.has(r.a.id)" :aria-label="`Visa resurser för ${r.a.name}`" :disabled="!r.people.length" @click="toggle(openAsg, r.a.id)">
                    <Icon name="chevron-right" :size="14" class="chev" />
                  </button>
                  <div class="a-info">
                    <button class="a-name ellipsis" @click="Object.assign(asgDlg, { open: true, assignment: r.a })">{{ r.a.name }}</button>
                    <span class="a-meta">
                      <span class="badge" :class="`st-${r.a.status}`">{{ STATUS[r.a.status] }}</span>
                      <span class="people" :title="r.people.map((p) => p.resource?.name).join(', ')">
                        <span v-for="pp in r.people.slice(0, 4)" :key="pp.allocation.id" class="pp">{{ pp.resource?.name.split(' ').map((x) => x[0]).join('').slice(0, 2) }}</span>
                        <span v-if="r.people.length > 4" class="pp more">+{{ r.people.length - 4 }}</span>
                      </span>
                    </span>
                  </div>
                </div>
                <div class="num v">{{ fmt(r.a.estimatedHours) }}</div>
                <div class="v prog">
                  <span class="num">{{ fmt(r.allocated) }}</span>
                  <div class="progress thin" :class="state(r.remaining)"><div class="fill" :style="{ width: progress(r) + '%' }" /></div>
                </div>
                <div class="v kv"><span class="chip kvar" :class="kvarClass(r.remaining)">{{ r.remaining === 0 ? '✓' : fmt(r.remaining) }}</span></div>
                <div v-for="(h, m) in r.months" :key="m" class="num v" :class="{ cur: m === currentMonth }">{{ h ? fmt(h) : '' }}</div>
              </div>
              <Expander :open="openAsg.has(r.a.id)">
                <div v-for="pp in r.people" :key="pp.allocation.id" class="g-row p-row">
                  <div class="c-name"><span class="indent" /><span class="ellipsis">{{ pp.resource?.name }}</span></div>
                  <div class="num v faint" />
                  <div class="num v">{{ fmt(sum(pp.allocation.hours)) }}</div>
                  <div class="num v faint" />
                  <div v-for="(h, m) in pp.allocation.hours" :key="m" class="num v" :class="{ cur: m === currentMonth }">{{ h ? fmt(h) : '' }}</div>
                </div>
              </Expander>
            </template>
          </Expander>
        </template>
        <div v-if="!groups.length" class="empty">
          <Icon name="briefcase" :size="36" class="icon" />
          <h3>Inga uppdrag</h3>
          <p>Justera filtren eller skapa ett nytt uppdrag.</p>
        </div>
      </div>
    </div>
    <AssignmentDialog v-if="asgDlg.open" :assignment="asgDlg.assignment" @close="asgDlg.open = false" />
  </div>
</template>

<style scoped>
.filters {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  flex-wrap: wrap;
  margin-bottom: var(--sp-3);
}
.search {
  position: relative;
  display: flex;
  align-items: center;
  width: 260px;
}
.search > .icon {
  position: absolute;
  left: 10px;
  color: var(--text-faint);
  pointer-events: none;
}
.search .input {
  padding-left: 30px;
  height: 30px;
}
.grid-wrap {
  max-height: calc(100dvh - var(--topbar-h) - var(--bottomnav-h) - 190px);
  min-height: 300px;
}
.asg-grid {
  --name-w: 280px;
  --cell-w: 56px;
  min-width: calc(var(--name-w) + 15 * var(--cell-w) + 60px);
}
.asg-grid .g-row {
  grid-template-columns: var(--name-w) 70px 86px 76px repeat(12, minmax(var(--cell-w), 1fr));
}
.v {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  padding: 0 10px;
  font-size: var(--fs-table);
}
.v.strong {
  font-weight: 650;
}
.v.cur {
  background: color-mix(in srgb, var(--accent) 6%, transparent);
}
.prog {
  flex-direction: column;
  align-items: stretch;
  justify-content: center;
  gap: 3px;
}
.prog .num {
  text-align: right;
}
.kv {
  justify-content: flex-end;
}
.a-row {
  min-height: 48px;
  border-bottom: 1px solid var(--border);
}
.a-row:hover,
.a-row:hover .c-name {
  background: var(--surface-2);
}
.p-row {
  min-height: 34px;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.p-row .c-name {
  background: var(--surface-2);
  font-size: var(--fs-table);
}
.exp {
  flex: none;
  width: 24px;
  height: 24px;
  display: grid;
  place-items: center;
  border: 0;
  background: transparent;
  color: var(--text-muted);
  border-radius: var(--r-sm);
  cursor: pointer;
}
.exp:disabled {
  opacity: 0.3;
  cursor: default;
}
.exp:not(:disabled):hover {
  background: var(--surface-3);
}
.a-info {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}
.a-name {
  border: 0;
  background: none;
  padding: 0;
  font: inherit;
  font-weight: 600;
  color: var(--text);
  text-align: left;
  cursor: pointer;
  font-size: var(--fs-table);
}
.a-name:hover {
  color: var(--accent);
  text-decoration: underline;
}
.a-meta {
  display: flex;
  align-items: center;
  gap: 6px;
}
.people {
  display: flex;
}
.pp {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 9px;
  font-weight: 700;
  background: var(--accent-soft);
  color: var(--accent);
  border: 2px solid var(--surface);
  margin-left: -5px;
}
.pp:first-child {
  margin-left: 0;
}
.pp.more {
  background: var(--surface-3);
  color: var(--text-muted);
}
.indent {
  width: 30px;
  align-self: stretch;
  border-right: 2px solid var(--accent-soft);
  margin-right: 8px;
  flex: none;
}
@media (max-width: 767px) {
  .asg-grid {
    --name-w: 170px;
  }
  .search {
    width: 100%;
  }
}
</style>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { api } from '../api.js'
import {
  store,
  index,
  thresholds,
  resourceAllocated,
  assignmentAllocatedTotal,
  assignmentRemaining,
} from '../store.js'
import { MONTHS, MONTH_NAMES, sum, sumMonths, occLevel, pct } from '../utils/calc.js'
import { fmt, fmtPct, fmtRelative, STATUS } from '../utils/format.js'
import OccCell from '../components/OccCell.vue'
import Expander from '../components/Expander.vue'
import CapacityChart from '../components/CapacityChart.vue'
import Icon from '../components/Icon.vue'

const year = computed(() => store.planningYear ?? store.year)
const currentMonth = computed(() => (year.value === new Date().getFullYear() ? new Date().getMonth() : -1))
const p = computed(() => store.planning)

const activeResources = computed(() => (p.value?.resources ?? []).filter((r) => r.isActive))
const capacity = computed(() => sumMonths(activeResources.value.map((r) => r.capacity)))
const allocated = computed(() => sumMonths(activeResources.value.map((r) => resourceAllocated(r.id))))
const kpi = computed(() => {
  const cap = sum(capacity.value)
  const alloc = sum(allocated.value)
  let overMonths = 0
  for (const r of activeResources.value) {
    const a = resourceAllocated(r.id)
    r.capacity.forEach((c, m) => {
      if (['near', 'over'].includes(occLevel(a[m], c, thresholds.value))) overMonths++
    })
  }
  const backlog = (p.value?.assignments ?? []).filter((a) => a.status === 'Backlogg')
  const unallocated = (p.value?.assignments ?? [])
    .filter((a) => a.status !== 'Klar')
    .reduce((s, a) => s + Math.max(0, assignmentRemaining(a)), 0)
  // Ledig kapacitet resten av året (från innevarande månad).
  const fromM = Math.max(0, currentMonth.value)
  const freeRest = sum(capacity.value.slice(fromM)) - sum(allocated.value.slice(fromM))
  return { cap, alloc, util: pct(alloc, cap), overMonths, backlog, unallocated, freeRest, fromM }
})
const utilLevel = computed(() => occLevel(kpi.value.alloc, kpi.value.cap, thresholds.value))

// ---------- Team-heatmap ----------
const openTeams = ref(new Set())
const teams = computed(() => {
  if (!p.value) return []
  const list = [...p.value.teams.map((t) => ({ ...t })), { id: 0, name: 'Utan team', color: 'var(--text-faint)' }]
  return list
    .map((t) => {
      const res = activeResources.value.filter((r) => (r.teamId ?? 0) === t.id)
      return {
        team: t,
        resources: res,
        capacity: sumMonths(res.map((r) => r.capacity)),
        allocated: sumMonths(res.map((r) => resourceAllocated(r.id))),
      }
    })
    .filter((g) => g.resources.length)
})
function toggleTeam(id) {
  const s = new Set(openTeams.value)
  s.has(id) ? s.delete(id) : s.add(id)
  openTeams.value = s
}

// ---------- Varningar ----------
const warnings = computed(() => {
  if (!p.value) return []
  const out = []
  for (const r of activeResources.value) {
    const a = resourceAllocated(r.id)
    const overM = []
    let maxP = 0
    r.capacity.forEach((c, m) => {
      const lv = occLevel(a[m], c, thresholds.value)
      if (lv === 'near' || lv === 'over') {
        overM.push(MONTHS[m])
        maxP = Math.max(maxP, pct(a[m], c))
      }
      if (c === 0 && a[m] > 0 && !r.absences.some((x) => x.month === m + 1)) {
        out.push({ level: 'over', icon: 'calendar', text: `${r.name} har ${a[m]} h planerat i ${MONTH_NAMES[m]} men ingen kapacitet (utanför anställningsperioden).` })
      }
    })
    if (overM.length)
      out.push({
        level: maxP > (thresholds.value?.thresholdCritical ?? 110) ? 'over' : 'near',
        icon: 'user',
        text: `${r.name} är överbelagd ${overM.length === 1 ? 'i' : `${overM.length} månader:`} ${overM.join(', ')} (max ${fmtPct(maxP)}).`,
      })
  }
  for (const asg of p.value.assignments) {
    const rem = assignmentRemaining(asg)
    if (rem < 0 && asg.status !== 'Klar')
      out.push({ level: 'near', icon: 'briefcase', text: `${asg.name} är överfördelat med ${fmt(-rem)} h (estimat ${fmt(asg.estimatedHours)} h).` })
    if (asg.status === 'Klar') {
      const rows = index.value.allocByAssignment.get(asg.id) ?? []
      const future = rows.reduce((s, r) => s + sum(r.hours.slice(Math.max(0, currentMonth.value))), 0)
      if (future > 0) out.push({ level: 'near', icon: 'briefcase', text: `${asg.name} är markerat Klar men har ${fmt(future)} h planerat framåt.` })
    }
  }
  return out.sort((a, b) => (a.level === b.level ? 0 : a.level === 'over' ? -1 : 1))
})
const showAllWarnings = ref(false)

// ---------- Backlogg & senaste ändringar ----------
const topBacklog = computed(() =>
  (p.value?.assignments ?? []).filter((a) => a.status === 'Backlogg').sort((a, b) => a.priority - b.priority).slice(0, 5),
)
const changes = ref([])
async function loadChanges() {
  try {
    changes.value = await api.get('/api/changes?take=6')
  } catch {
    changes.value = []
  }
}
onMounted(loadChanges)
watch(() => store.planning?.lastChangeId, loadChanges)
</script>

<template>
  <div class="page">
    <div class="page-head">
      <div>
        <h1>Översikt {{ year }}</h1>
        <p class="sub">{{ p?.settings.organizationName }} · {{ activeResources.length }} resurser i {{ p?.teams.length ?? 0 }} team</p>
      </div>
      <span class="spacer" />
      <RouterLink to="/planering" class="btn primary"><Icon name="table" :size="16" /> Öppna planeringen</RouterLink>
    </div>

    <div v-if="!p" class="kpis">
      <div v-for="i in 5" :key="i" class="skeleton" style="height: 92px; border-radius: 10px" />
    </div>

    <template v-else>
      <section class="kpis" aria-label="Nyckeltal">
        <div class="kpi card">
          <span class="k-label">Beläggning {{ year }}</span>
          <span class="k-value" :class="`lv-${utilLevel}`">{{ fmtPct(kpi.util) }}</span>
          <div class="progress thin" :class="{ over: kpi.util > 100, done: kpi.util >= (thresholds?.thresholdLow ?? 80) && kpi.util <= 100 }">
            <div class="fill" :style="{ width: Math.min(100, kpi.util) + '%' }" />
          </div>
          <span class="k-sub">{{ fmt(kpi.alloc) }} av {{ fmt(kpi.cap) }} h planerat</span>
        </div>
        <div class="kpi card">
          <span class="k-label">Ledig kapacitet {{ kpi.fromM > 0 ? `${MONTHS[kpi.fromM]}–DEC` : 'helår' }}</span>
          <span class="k-value" :class="{ 'danger-text': kpi.freeRest < 0 }">{{ fmt(kpi.freeRest) }} h</span>
          <span class="k-sub">≈ {{ fmt(Math.max(0, kpi.freeRest) / Math.max(1, (p.settings.standardCapacity * p.settings.planningCeilingPercent) / 100) / Math.max(1, 12 - kpi.fromM)) }} heltidsresurser per månad</span>
        </div>
        <div class="kpi card">
          <span class="k-label">Att fördela</span>
          <span class="k-value">{{ fmt(kpi.unallocated) }} h</span>
          <span class="k-sub">{{ kpi.backlog.length }} uppdrag i backloggen</span>
        </div>
        <div class="kpi card">
          <span class="k-label">Överbelagda resursmånader</span>
          <span class="k-value" :class="{ 'danger-text': kpi.overMonths > 0, 'success-text': kpi.overMonths === 0 }">
            {{ kpi.overMonths === 0 ? '✓ 0' : `▲ ${kpi.overMonths}` }}
          </span>
          <span class="k-sub">över {{ thresholds?.thresholdHigh ?? 100 }} % av kapaciteten</span>
        </div>
      </section>

      <div class="cols">
        <section class="card chart-card">
          <header class="card-head">
            <h3>Kapacitet och planerat per månad</h3>
            <span class="spacer" />
            <span class="muted small">Alla aktiva resurser</span>
          </header>
          <div class="card-body">
            <CapacityChart :allocated="allocated" :capacity="capacity" :current-month="currentMonth" />
          </div>
        </section>

        <section class="card">
          <header class="card-head">
            <Icon name="alert" :size="16" />
            <h3>Att hålla koll på</h3>
            <span class="spacer" />
            <span class="chip">{{ warnings.length }}</span>
          </header>
          <div class="card-body warn-body">
            <p v-if="!warnings.length" class="success-text">✓ Inga överbeläggningar eller överfördelade uppdrag.</p>
            <ul v-else class="warn-list">
              <li v-for="(w, i) in showAllWarnings ? warnings : warnings.slice(0, 6)" :key="i" :class="w.level">
                <span class="w-ico"><Icon :name="w.icon" :size="14" /></span>
                <span>{{ w.text }}</span>
              </li>
            </ul>
            <button v-if="warnings.length > 6" class="btn ghost sm" @click="showAllWarnings = !showAllWarnings">
              {{ showAllWarnings ? 'Visa färre' : `Visa alla ${warnings.length}` }}
            </button>
          </div>
        </section>
      </div>

      <section class="card heat">
        <header class="card-head">
          <h3>Beläggning per team</h3>
          <span class="spacer" />
          <span class="legend small muted">
            <span class="lg lv-low" /> Ledigt <span class="lg lv-ok" /> Bra <span class="lg lv-near" /> Överbelagd <span class="lg lv-over" /> Kraftigt
          </span>
        </header>
        <div class="grid-wrap flat">
          <div class="grid">
            <div class="g-row g-head">
              <div class="c-name">Team / resurs</div>
              <div v-for="(m, i) in MONTHS" :key="m" class="mh" :class="{ current: i === currentMonth }">{{ m }}</div>
              <div class="mh c-year">År</div>
            </div>
            <template v-for="g in teams" :key="g.team.id">
              <div class="g-row team-row">
                <div class="c-name">
                  <button class="team-toggle" :aria-expanded="openTeams.has(g.team.id)" @click="toggleTeam(g.team.id)">
                    <Icon name="chevron-right" :size="16" class="chev" />
                    <span class="team-swatch" :style="{ background: g.team.color }" />
                    <span class="ellipsis">{{ g.team.name }}</span>
                    <span class="chip">{{ g.resources.length }}</span>
                  </button>
                </div>
                <div v-for="(c, m) in g.capacity" :key="m" class="c-occ">
                  <OccCell :allocated="g.allocated[m]" :capacity="c" :month="m" :label="g.team.name" mode="percent" :thresholds="thresholds" :current="m === currentMonth" />
                </div>
                <div class="c-year">
                  <OccCell :allocated="sum(g.allocated)" :capacity="sum(g.capacity)" :label="`${g.team.name}, helår`" mode="percent" :thresholds="thresholds" />
                </div>
              </div>
              <Expander :open="openTeams.has(g.team.id)">
                <div v-for="r in g.resources" :key="r.id" class="g-row res-mini">
                  <div class="c-name">
                    <span class="indent" />
                    <span class="ellipsis">{{ r.name }}</span>
                    <span class="faint xs ellipsis">{{ r.role }}</span>
                  </div>
                  <div v-for="(c, m) in r.capacity" :key="m" class="c-occ">
                    <OccCell :allocated="resourceAllocated(r.id)[m]" :capacity="c" :month="m" :label="r.name" mode="percent" :thresholds="thresholds" />
                  </div>
                  <div class="c-year">
                    <OccCell :allocated="sum(resourceAllocated(r.id))" :capacity="sum(r.capacity)" :label="`${r.name}, helår`" mode="percent" :thresholds="thresholds" />
                  </div>
                </div>
              </Expander>
            </template>
          </div>
        </div>
      </section>

      <div class="cols">
        <section class="card">
          <header class="card-head">
            <Icon name="inbox" :size="16" />
            <h3>Backlogg – högst prioriterat</h3>
            <span class="spacer" />
            <RouterLink to="/backlogg" class="btn ghost sm">Visa alla <Icon name="arrow-right" :size="14" /></RouterLink>
          </header>
          <ol v-if="topBacklog.length" class="list">
            <li v-for="(a, i) in topBacklog" :key="a.id">
              <span class="rank num">{{ i + 1 }}</span>
              <span class="ellipsis grow">{{ a.name }}</span>
              <span class="muted small ellipsis">{{ a.objectId ? index.objects.get(a.objectId)?.name : '' }}</span>
              <span class="chip kvar pos num">{{ fmt(assignmentRemaining(a)) }} h</span>
            </li>
          </ol>
          <p v-else class="card-body muted">Backloggen är tom.</p>
        </section>

        <section class="card">
          <header class="card-head">
            <Icon name="clock" :size="16" />
            <h3>Senaste ändringar</h3>
            <span class="spacer" />
            <RouterLink to="/andringar" class="btn ghost sm">Ändringslogg <Icon name="arrow-right" :size="14" /></RouterLink>
          </header>
          <ul v-if="changes.length" class="list changes">
            <li v-for="c in changes" :key="c.id">
              <span class="avatar">{{ c.user.slice(0, 1) }}</span>
              <span class="grow">
                <span class="c-sum">{{ c.summary }}</span>
                <span class="faint xs">{{ c.user }} · {{ fmtRelative(c.timestamp) }}</span>
              </span>
            </li>
          </ul>
          <p v-else class="card-body muted">Inga ändringar ännu.</p>
        </section>
      </div>
    </template>
  </div>
</template>

<style scoped>
.kpis {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(210px, 45%), 1fr));
  gap: var(--sp-3);
  margin-bottom: var(--sp-4);
}
.kpi {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: var(--sp-4);
}
.k-label {
  font-size: var(--fs-sm);
  color: var(--text-muted);
  font-weight: 600;
}
.k-value {
  font-size: 28px;
  font-weight: 650;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
  letter-spacing: -0.01em;
}
.k-value.lv-ok {
  color: var(--occ-ok-fg);
}
.k-value.lv-near,
.k-value.lv-over {
  color: var(--occ-over-fg);
}
.k-sub {
  font-size: var(--fs-sm);
  color: var(--text-muted);
}
.cols {
  display: grid;
  grid-template-columns: minmax(0, 3fr) minmax(0, 2fr);
  gap: var(--sp-3);
  margin-bottom: var(--sp-4);
}
.cols > .card {
  min-width: 0;
}
.warn-body {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 320px;
  overflow: auto;
}
.warn-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.warn-list li {
  display: flex;
  gap: 10px;
  align-items: flex-start;
  font-size: var(--fs-table);
}
.w-ico {
  flex: none;
  width: 24px;
  height: 24px;
  display: grid;
  place-items: center;
  border-radius: var(--r-sm);
  background: var(--occ-near-bg);
  color: var(--occ-near-fg);
}
.warn-list li.over .w-ico {
  background: var(--occ-over-bg);
  color: var(--occ-over-fg);
}
.heat {
  margin-bottom: var(--sp-4);
  overflow: hidden;
}
.grid-wrap.flat {
  border: 0;
  border-radius: 0;
  box-shadow: none;
}
.res-mini {
  min-height: 36px;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.res-mini .c-name {
  background: var(--surface-2);
  font-size: var(--fs-table);
}
.res-mini > .c-occ,
.res-mini > .c-year {
  padding: 3px 2px;
}
.indent {
  width: 22px;
  align-self: stretch;
  border-right: 2px solid var(--accent-soft);
  margin-right: 6px;
  flex: none;
}
.legend {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.lg {
  width: 12px;
  height: 12px;
  border-radius: 3px;
  display: inline-block;
  margin-left: 6px;
}
.lg.lv-low {
  background: var(--occ-low-bg);
  border: 1px solid var(--occ-low-bar);
}
.lg.lv-ok {
  background: var(--occ-ok-bg);
  border: 1px solid var(--occ-ok-bar);
}
.lg.lv-near {
  background: var(--occ-near-bg);
  border: 1px solid var(--occ-near-bar);
}
.lg.lv-over {
  background: var(--occ-over-bg);
  border: 1px solid var(--occ-over-bar);
}
.list {
  list-style: none;
  margin: 0;
  padding: 4px 0;
}
.list li {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px var(--sp-4);
  border-bottom: 1px solid var(--border);
  min-width: 0;
}
.list li:last-child {
  border-bottom: 0;
}
.grow {
  flex: 1;
  min-width: 0;
}
.rank {
  min-width: 22px;
  height: 22px;
  border-radius: var(--r-sm);
  background: var(--surface-2);
  display: grid;
  place-items: center;
  font-size: var(--fs-xs);
  font-weight: 700;
  color: var(--text-muted);
}
.changes .grow {
  display: flex;
  flex-direction: column;
}
.c-sum {
  font-size: var(--fs-table);
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}
.avatar {
  flex: none;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--accent-soft);
  color: var(--accent);
  font-weight: 700;
  font-size: var(--fs-sm);
}
@media (max-width: 1023px) {
  .cols {
    grid-template-columns: minmax(0, 1fr);
  }
}
@media (max-width: 767px) {
  .kpi {
    padding: var(--sp-3);
  }
  .k-value {
    font-size: 22px;
  }
  .k-sub {
    font-size: var(--fs-xs);
  }
}
</style>

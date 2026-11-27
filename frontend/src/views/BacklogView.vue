<script setup>
import { computed, reactive } from 'vue'
import { store, canEdit, resourceAllocated, assignmentRemaining } from '../store.js'
import { MONTHS, sum } from '../utils/calc.js'
import { fmt } from '../utils/format.js'
import BacklogPanel from '../components/BacklogPanel.vue'
import AllocationDialog from '../components/AllocationDialog.vue'
import AssignmentDialog from '../components/AssignmentDialog.vue'
import Icon from '../components/Icon.vue'

const year = computed(() => store.planningYear ?? store.year)
const fromM = computed(() => (year.value === new Date().getFullYear() ? new Date().getMonth() : 0))

// "Räcker folket?" – backloggens behov mot ledig kapacitet resten av året, per team.
const teams = computed(() => {
  const p = store.planning
  if (!p) return []
  const rows = [...p.teams, { id: 0, name: 'Utan team', color: 'var(--text-faint)' }].map((t) => {
    const res = p.resources.filter((r) => r.isActive && (r.teamId ?? 0) === t.id)
    const free = res.reduce((s, r) => {
      const a = resourceAllocated(r.id)
      return s + r.capacity.slice(fromM.value).reduce((x, c, i) => x + Math.max(0, c - a[fromM.value + i]), 0)
    }, 0)
    return { team: t, count: res.length, free }
  })
  return rows.filter((r) => r.count)
})
const freeTotal = computed(() => sum(teams.value.map((t) => t.free)))
const demand = computed(() =>
  (store.planning?.assignments ?? [])
    .filter((a) => a.status !== 'Klar')
    .reduce((s, a) => s + Math.max(0, assignmentRemaining(a)), 0),
)
const coverage = computed(() => (demand.value ? Math.min(100, (freeTotal.value / demand.value) * 100) : 100))
const maxFree = computed(() => Math.max(1, ...teams.value.map((t) => t.free)))

const assignDlg = reactive({ open: false, assignmentId: null })
const asgDlg = reactive({ open: false, assignment: null })
</script>

<template>
  <div class="page">
    <div class="page-head">
      <div>
        <h1>Backlogg</h1>
        <p class="sub">Prioritera nya uppdrag och fördela timmarna på resurser.</p>
      </div>
      <span class="spacer" />
      <RouterLink to="/planering" class="btn"><Icon name="table" :size="16" /> Planering med drag-n-drop</RouterLink>
      <button v-if="canEdit" class="btn primary" @click="Object.assign(asgDlg, { open: true, assignment: null })">
        <Icon name="plus" :size="16" /> Nytt uppdrag
      </button>
    </div>

    <div class="cols">
      <div class="card main">
        <div class="card-body">
          <BacklogPanel
            @assign="(a) => Object.assign(assignDlg, { open: true, assignmentId: a.id })"
            @edit="(a) => Object.assign(asgDlg, { open: true, assignment: a })"
            @create="Object.assign(asgDlg, { open: true, assignment: null })"
          />
        </div>
      </div>

      <aside class="card side">
        <header class="card-head">
          <Icon name="target" :size="16" />
          <h3>Räcker kapaciteten?</h3>
        </header>
        <div class="card-body stack">
          <div class="cover">
            <div class="cv">
              <span class="muted small">Ledig kapacitet {{ MONTHS[fromM] }}–DEC {{ year }}</span>
              <strong class="num">{{ fmt(freeTotal) }} h</strong>
            </div>
            <div class="cv">
              <span class="muted small">Ej fördelat (alla aktiva uppdrag)</span>
              <strong class="num">{{ fmt(demand) }} h</strong>
            </div>
          </div>
          <div>
            <div class="progress" :class="{ done: coverage >= 100, over: coverage < 60 }">
              <div class="fill" :style="{ width: coverage + '%' }" />
            </div>
            <p class="small" style="margin-top: 6px">
              <template v-if="freeTotal >= demand">✓ Ledig kapacitet täcker det som återstår att fördela.</template>
              <template v-else>▲ {{ fmt(demand - freeTotal) }} h saknas – prioritera, skjut fram eller ta in fler resurser.</template>
            </p>
          </div>
          <h4 class="sub-h">Ledigt per team</h4>
          <ul class="teams">
            <li v-for="t in teams" :key="t.team.id">
              <span class="team-swatch" :style="{ background: t.team.color }" />
              <span class="grow ellipsis">{{ t.team.name }}</span>
              <span class="bar"><span :style="{ width: (t.free / maxFree) * 100 + '%' }" /></span>
              <span class="num small">{{ fmt(t.free) }} h</span>
            </li>
          </ul>
        </div>
      </aside>
    </div>

    <AllocationDialog v-if="assignDlg.open" :assignment-id="assignDlg.assignmentId" @close="assignDlg.open = false" />
    <AssignmentDialog v-if="asgDlg.open" :assignment="asgDlg.assignment" @close="asgDlg.open = false" />
  </div>
</template>

<style scoped>
.cols {
  display: grid;
  grid-template-columns: minmax(0, 1.6fr) minmax(280px, 1fr);
  gap: var(--sp-4);
  align-items: start;
}
.side {
  position: sticky;
  top: calc(var(--topbar-h) + 16px);
}
.cover {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--sp-3);
}
.cv {
  display: flex;
  flex-direction: column;
}
.cv strong {
  font-size: var(--fs-xl);
  text-align: left;
}
.sub-h {
  font-size: var(--fs-sm);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
}
.teams {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.teams li {
  display: flex;
  align-items: center;
  gap: 8px;
}
.team-swatch {
  width: 10px;
  height: 10px;
  border-radius: 3px;
  flex: none;
}
.grow {
  flex: 1;
  min-width: 0;
}
.bar {
  width: 90px;
  height: 6px;
  border-radius: 3px;
  background: var(--surface-3);
  overflow: hidden;
}
.bar > span {
  display: block;
  height: 100%;
  background: var(--occ-ok-bar);
  border-radius: 3px;
  transition: width var(--dur-3) var(--ease-out);
}
@media (max-width: 1023px) {
  .cols {
    grid-template-columns: minmax(0, 1fr);
  }
  .side {
    position: static;
  }
}
</style>

<script setup>
// Tilldela ett uppdrag till en resurs (efter drag-n-drop eller via "Tilldela"). Förhandsvisar fördelningen per månad.
import { computed, ref, watch } from 'vue'
import {
  store,
  index,
  thresholds,
  resourceAllocated,
  assignmentAllocatedTotal,
  createAllocation,
  toast,
  toastError,
} from '../store.js'
import { MONTHS, distribute, sum, parseHours } from '../utils/calc.js'
import { fmt } from '../utils/format.js'
import Modal from './Modal.vue'
import OccCell from './OccCell.vue'

const props = defineProps({
  assignmentId: { type: Number, default: null },
  resourceId: { type: Number, default: null },
})
const emit = defineEmits(['close', 'done'])

const resourceId = ref(props.resourceId)
const assignmentId = ref(props.assignmentId)
const resource = computed(() => index.value?.resources.get(resourceId.value))
const assignment = computed(() => index.value?.assignments.get(assignmentId.value))
const existing = computed(() =>
  store.planning?.allocations.find((a) => a.resourceId === resourceId.value && a.assignmentId === assignmentId.value),
)

const resources = computed(() =>
  (store.planning?.resources ?? [])
    .filter((r) => r.isActive)
    .map((r) => {
      const free = sum(r.capacity) - sum(resourceAllocated(r.id))
      return { ...r, free }
    }),
)
const assignments = computed(() =>
  (store.planning?.assignments ?? []).filter((a) => a.status !== 'Klar'),
)

const year = computed(() => store.planningYear ?? store.year)
const now = new Date()
const defaultFrom = () => {
  const a = assignment.value
  let from = year.value === now.getFullYear() ? now.getMonth() : year.value > now.getFullYear() ? 0 : 0
  if (a?.startDate) {
    const d = new Date(a.startDate)
    if (d.getFullYear() === year.value) from = Math.max(from, d.getMonth())
  }
  return from
}
const defaultTo = () => {
  const a = assignment.value
  if (a?.endDate) {
    const d = new Date(a.endDate)
    if (d.getFullYear() === year.value) return d.getMonth()
  }
  return 11
}

const mode = ref('free')
const from = ref(defaultFrom())
const to = ref(defaultTo())
const remaining = computed(() => (assignment.value ? assignment.value.estimatedHours - assignmentAllocatedTotal(assignment.value) : 0))
const hours = ref(new Array(12).fill(0))

const capacity = computed(() => resource.value?.capacity ?? new Array(12).fill(0))
const allocated = computed(() => (resource.value ? resourceAllocated(resource.value.id) : new Array(12).fill(0)))
const freeByMonth = computed(() => capacity.value.map((c, m) => Math.max(0, c - allocated.value[m])))
const freeInRange = computed(() => {
  let s = 0
  for (let m = Math.min(from.value, to.value); m <= Math.max(from.value, to.value); m++) s += freeByMonth.value[m]
  return s
})
// Standard: fyll resursens lediga tid i perioden, men aldrig mer än det som är kvar på uppdraget.
const suggestedTotal = () => {
  const kvar = Math.max(0, remaining.value)
  return resource.value && freeInRange.value > 0 ? Math.min(kvar, freeInRange.value) : kvar
}
const total = ref(suggestedTotal())

function recalc() {
  const months = []
  for (let m = Math.min(from.value, to.value); m <= Math.max(from.value, to.value); m++) months.push(m)
  if (mode.value === 'none') hours.value = new Array(12).fill(0)
  else if (mode.value === 'even') hours.value = distribute(total.value, months)
  else if (mode.value === 'capacity') hours.value = distribute(total.value, months, capacity.value)
  else hours.value = distribute(total.value, months, freeByMonth.value)
}
watch([mode, from, to, total, resourceId, assignmentId], recalc, { immediate: true })
watch(assignmentId, () => {
  from.value = defaultFrom()
  to.value = defaultTo()
  total.value = suggestedTotal()
})
watch(resourceId, () => (total.value = suggestedTotal()))

const totalPreview = computed(() => sum(hours.value))
const remainingAfter = computed(() => remaining.value - totalPreview.value)
const overMonths = computed(() => hours.value.filter((h, m) => h > 0 && allocated.value[m] + h > capacity.value[m]).length)

function setHour(m, e) {
  const v = parseHours(e.target.value)
  if (Number.isNaN(v)) return
  const next = [...hours.value]
  next[m] = v
  hours.value = next
}

const busy = ref(false)
async function submit() {
  if (!resource.value || !assignment.value) return toast('Välj resurs och uppdrag.', { type: 'error' })
  busy.value = true
  try {
    await createAllocation(resource.value.id, assignment.value.id, hours.value)
    toast(
      `${assignment.value.name} tilldelades ${resource.value.name}${totalPreview.value ? ` (${fmt(totalPreview.value)} h)` : ''}.`,
      { type: 'success' },
    )
    emit('done', { resourceId: resource.value.id, assignmentId: assignment.value.id })
    emit('close')
  } catch (e) {
    toastError(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <Modal
    title="Tilldela uppdrag"
    :subtitle="resource && assignment ? `${assignment.name} → ${resource.name} · ${year}` : `Planeringsår ${year}`"
    width="760px"
    @close="emit('close')"
  >
    <div class="stack">
      <div v-if="!props.assignmentId || !props.resourceId" class="form-grid pickers">
        <label v-if="!props.assignmentId" class="field">
          <span>Uppdrag</span>
          <select v-model="assignmentId" class="input">
            <option :value="null" disabled>Välj uppdrag…</option>
            <option v-for="a in assignments" :key="a.id" :value="a.id">{{ a.name }}</option>
          </select>
        </label>
        <label v-if="!props.resourceId" class="field">
          <span>Resurs</span>
          <select v-model="resourceId" class="input">
            <option :value="null" disabled>Välj resurs…</option>
            <option v-for="r in resources" :key="r.id" :value="r.id">
              {{ r.name }} – {{ r.free >= 0 ? `${fmt(r.free)} h ledigt` : `${fmt(-r.free)} h överbelagd` }}
            </option>
          </select>
        </label>
      </div>

      <div v-if="assignment" class="summary">
        <div>
          <span class="muted small">Estimat</span>
          <strong class="num">{{ fmt(assignment.estimatedHours) }} h</strong>
        </div>
        <div>
          <span class="muted small">Kvar att fördela</span>
          <strong class="num" :class="{ 'danger-text': remaining < 0 }">{{ fmt(remaining) }} h</strong>
        </div>
        <div>
          <span class="muted small">Efter tilldelning</span>
          <strong class="num" :class="remainingAfter < 0 ? 'danger-text' : remainingAfter === 0 ? 'success-text' : ''">
            {{ remainingAfter === 0 ? '✓ 0' : fmt(remainingAfter) }} h
          </strong>
        </div>
      </div>

      <p v-if="existing" class="note">
        {{ resource?.name }} har redan detta uppdrag – timmarna läggs till på befintlig rad.
      </p>

      <div class="controls">
        <div class="field">
          <span>Fördelning</span>
          <div class="segmented" role="group" aria-label="Fördelningssätt">
            <button type="button" :aria-pressed="mode === 'free'" @click="mode = 'free'">Efter ledig tid</button>
            <button type="button" :aria-pressed="mode === 'even'" @click="mode = 'even'">Jämnt</button>
            <button type="button" :aria-pressed="mode === 'capacity'" @click="mode = 'capacity'">Efter kapacitet</button>
            <button type="button" :aria-pressed="mode === 'none'" @click="mode = 'none'">Tom rad</button>
          </div>
        </div>
        <label v-if="mode !== 'none'" class="field total">
          <span>Timmar att fördela</span>
          <input v-model.number="total" class="input num" type="number" min="0" step="1" />
        </label>
        <label v-if="mode !== 'none'" class="field">
          <span>Från</span>
          <select v-model.number="from" class="input">
            <option v-for="(m, i) in MONTHS" :key="m" :value="i">{{ m }}</option>
          </select>
        </label>
        <label v-if="mode !== 'none'" class="field">
          <span>Till</span>
          <select v-model.number="to" class="input">
            <option v-for="(m, i) in MONTHS" :key="m" :value="i">{{ m }}</option>
          </select>
        </label>
      </div>

      <p v-if="mode !== 'none' && resource" class="quick small">
        <span class="muted">Snabbval:</span>
        <button type="button" class="chip" @click="total = freeInRange">Ledig tid i perioden ({{ fmt(freeInRange) }} h)</button>
        <button type="button" class="chip" @click="total = Math.max(0, remaining)">Hela kvarvarande ({{ fmt(Math.max(0, remaining)) }} h)</button>
      </p>

      <div class="preview-wrap">
        <div class="preview">
          <div class="lbl" />
          <div v-for="m in MONTHS" :key="m" class="mh">{{ m }}</div>
          <div class="mh">Σ</div>

          <div class="lbl">Timmar</div>
          <input
            v-for="(h, i) in hours"
            :key="i"
            class="cell-input num"
            inputmode="numeric"
            :value="h || ''"
            :aria-label="`${MONTHS[i]} timmar`"
            @change="setHour(i, $event)"
          />
          <div class="sum num">{{ fmt(totalPreview) }}</div>

          <template v-if="resource">
            <div class="lbl">Beläggning</div>
            <OccCell
              v-for="(c, i) in capacity"
              :key="'o' + i"
              :allocated="allocated[i] + hours[i]"
              :capacity="c"
              :month="i"
              :label="resource.name"
              :thresholds="thresholds"
              mode="free"
            />
            <div class="sum num muted small">ledigt</div>
          </template>
        </div>
      </div>
      <p v-if="overMonths" class="warning-text small">
        ▲ {{ overMonths }} {{ overMonths === 1 ? 'månad blir' : 'månader blir' }} överbelagd för {{ resource?.name }}.
      </p>
    </div>

    <template #footer>
      <button class="btn" @click="emit('close')">Avbryt</button>
      <button class="btn primary" :disabled="busy || !resource || !assignment" @click="submit">
        Tilldela{{ totalPreview ? ` ${fmt(totalPreview)} h` : '' }}
      </button>
    </template>
  </Modal>
</template>

<style scoped>
.pickers {
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
}
.summary {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--sp-3);
  padding: var(--sp-3);
  background: var(--surface-2);
  border-radius: var(--r-md);
}
.summary > div {
  display: flex;
  flex-direction: column;
}
.summary strong {
  font-size: var(--fs-xl);
  text-align: left;
}
.quick {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.note {
  padding: 8px 12px;
  border-radius: var(--r-md);
  background: var(--accent-soft);
  font-size: var(--fs-sm);
}
.controls {
  display: flex;
  flex-wrap: wrap;
  gap: var(--sp-3);
  align-items: flex-end;
}
.controls .field {
  flex: 0 0 auto;
}
.controls .total {
  width: 140px;
}
.controls select {
  width: 90px;
}
.preview-wrap {
  overflow-x: auto;
  margin: 0 -4px;
  padding: 0 4px 4px;
}
.preview {
  display: grid;
  grid-template-columns: 72px repeat(12, minmax(40px, 1fr)) 50px;
  gap: 3px;
  align-items: stretch;
  min-width: 640px;
}
.mh {
  font-size: var(--fs-xs);
  font-weight: 700;
  color: var(--text-muted);
  text-align: right;
  padding: 0 6px;
}
.lbl {
  font-size: var(--fs-sm);
  color: var(--text-muted);
  font-weight: 600;
  display: flex;
  align-items: center;
}
.cell-input {
  width: 100%;
  height: 32px;
  border: 1px solid var(--border);
  border-radius: var(--r-sm);
  background: var(--surface);
  color: var(--text);
  font: inherit;
  font-size: var(--fs-table);
  padding: 0 6px;
}
.cell-input:focus {
  outline: none;
  border-color: var(--accent);
  box-shadow: inset 0 0 0 1px var(--accent);
}
.sum {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  font-weight: 700;
}
@media (max-width: 767px) {
  .summary strong {
    font-size: var(--fs-lg);
  }
}
</style>

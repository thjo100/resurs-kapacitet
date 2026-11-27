<script setup>
// En resurs i planeringsgriden: beläggningsrad + (expanderat) uppdragsrader med redigerbara timceller.
import { computed } from 'vue'
import {
  store,
  index,
  thresholds,
  setCell,
  undoLastCell,
  resourceAllocated,
  assignmentRemaining,
  toast,
} from '../store.js'
import { MONTHS, MONTH_NAMES, sum, parseHours, parseClipboard, fillRight, initials } from '../utils/calc.js'
import { fmt } from '../utils/format.js'
import { move, caretAtStart, caretAtEnd, pasteTargets } from '../utils/gridNav.js'
import { vDropTarget } from '../utils/dnd.js'
import OccCell from './OccCell.vue'
import Expander from './Expander.vue'
import Icon from './Icon.vue'

const props = defineProps({
  resource: { type: Object, required: true },
  open: { type: Boolean, default: false },
  mode: { type: String, default: 'hours' },
  editable: { type: Boolean, default: false },
  currentMonth: { type: Number, default: -1 },
  flashAllocId: { type: Number, default: null },
  activeAllocId: { type: Number, default: null },
})
const emit = defineEmits(['toggle', 'drop', 'add', 'absence', 'row-menu', 'focus-cell', 'edit-assignment'])

const rows = computed(() => {
  const list = index.value?.allocByResource.get(props.resource.id) ?? []
  return [...list].sort((a, b) => {
    const pa = index.value.assignments.get(a.assignmentId)?.priority ?? 0
    const pb = index.value.assignments.get(b.assignmentId)?.priority ?? 0
    return pa - pb
  })
})
const allocated = computed(() => resourceAllocated(props.resource.id))
const capacity = computed(() => props.resource.capacity)
const yearAllocated = computed(() => sum(allocated.value))
const yearCapacity = computed(() => sum(capacity.value))
const team = computed(() => (props.resource.teamId ? index.value?.teams.get(props.resource.teamId) : null))
const absenceBy = computed(() => new Map(props.resource.absences.map((a) => [a.month - 1, a])))
const regionId = computed(() => `res-${props.resource.id}-rows`)

const asg = (a) => index.value?.assignments.get(a.assignmentId)
const obj = (a) => {
  const x = asg(a)
  return x?.objectId ? index.value.objects.get(x.objectId) : null
}
const kvar = (a) => {
  const x = asg(a)
  return x ? assignmentRemaining(x) : 0
}
const kvarClass = (v) => (v < 0 ? 'neg' : v === 0 ? 'zero' : 'pos')

// ---------- Cellredigering ----------
function container(el) {
  return el.closest('.grid')
}

function commit(a, m, input) {
  const v = parseHours(input.value)
  if (Number.isNaN(v)) {
    input.classList.add('invalid')
    toast('Ange ett heltal mellan 0 och 744.', { type: 'error', timeout: 3000 })
    input.value = a.hours[m] || ''
    setTimeout(() => input.classList.remove('invalid'), 1500)
    return
  }
  setCell(a, m, v)
  input.value = v || ''
}

function onFocus(a, m, e) {
  e.target.select()
  store.focus = { allocationId: a.id, month: m }
  emit('focus-cell', { allocation: a, month: m })
}
function onBlur(a, m, e) {
  commit(a, m, e.target)
  store.focus = null
}

function onKey(a, m, e) {
  const input = e.target
  const c = container(input)
  const go = (dr, dc) => {
    commit(a, m, input)
    move(c, input, dr, dc)
  }
  if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
    e.preventDefault()
    commit(a, m, input)
    fillRow(a, m)
    return
  }
  switch (e.key) {
    case 'Enter':
      e.preventDefault()
      go(e.shiftKey ? -1 : 1, 0)
      break
    case 'ArrowDown':
      e.preventDefault()
      go(1, 0)
      break
    case 'ArrowUp':
      e.preventDefault()
      go(-1, 0)
      break
    case 'ArrowLeft':
      if (caretAtStart(input) || input.selectionEnd - input.selectionStart === input.value.length) {
        e.preventDefault()
        go(0, -1)
      }
      break
    case 'ArrowRight':
      if (caretAtEnd(input) || input.selectionEnd - input.selectionStart === input.value.length) {
        e.preventDefault()
        go(0, 1)
      }
      break
    case 'Escape':
      input.value = a.hours[m] || ''
      input.select()
      break
    case 'z':
    case 'Z':
      if ((e.ctrlKey || e.metaKey) && String(a.hours[m] || '') === input.value) {
        e.preventDefault()
        if (undoLastCell()) toast('Ångrade senaste ändringen.', { timeout: 1500 })
      }
      break
  }
}

function fillRow(a, m) {
  const next = fillRight(a.hours, m)
  next.forEach((h, i) => setCell(a, i, h))
  toast(`Fyllde ${MONTHS[m]} → DEC med ${a.hours[m] || 0} h.`, { timeout: 2500 })
}

function onPaste(a, m, e) {
  const text = e.clipboardData?.getData('text/plain') ?? ''
  if (!/[\t\n]/.test(text.trim())) return // enstaka värde – låt webbläsaren hantera
  e.preventDefault()
  const matrix = parseClipboard(text)
  const targets = pasteTargets(container(e.target), e.target, matrix)
  const invalid = matrix.flat().filter((v) => Number.isNaN(v)).length
  for (const t of targets) {
    const alloc = store.planning.allocations.find((x) => x.id === t.allocId)
    if (!alloc) continue
    setCell(alloc, t.col, t.value)
    if (t.el) {
      t.el.classList.remove('flash')
      void t.el.offsetWidth
      t.el.classList.add('flash')
    }
  }
  toast(`Klistrade in ${targets.length} celler${invalid ? ` (${invalid} ogiltiga hoppades över)` : ''}.`, {
    type: invalid ? 'error' : 'success',
    timeout: 3000,
  })
}

const cellLabel = (a, m) => `${props.resource.name}, ${asg(a)?.name ?? ''}, ${MONTH_NAMES[m]}, timmar`
</script>

<template>
  <div class="res" :class="{ open }">
    <div
      v-drop-target="{ disabled: !editable, onDrop: (id) => emit('drop', id) }"
      class="g-row res-row"
      :data-drop-label="`Släpp för att tilldela ${resource.name}`"
    >
      <div class="c-name">
        <button class="res-toggle" :aria-expanded="open" :aria-controls="regionId" @click="emit('toggle')">
          <Icon name="chevron-right" :size="16" class="chev" />
          <span class="avatar" :style="{ '--c': team?.color }">{{ initials(resource.name) }}</span>
          <span class="nm">
            <strong class="ellipsis">{{ resource.name }}</strong>
            <span class="sub ellipsis">
              {{ resource.role || 'Resurs' }}<template v-if="resource.employmentPercent < 100"> · {{ resource.employmentPercent }} %</template>
              · {{ rows.length }} {{ rows.length === 1 ? 'uppdrag' : 'uppdrag' }}
            </span>
          </span>
        </button>
        <span v-if="resource.type === 'Konsult'" class="chip" title="Konsult">K</span>
        <span v-if="!resource.isActive" class="chip" title="Inaktiv">Inaktiv</span>
      </div>
      <div v-for="(c, m) in capacity" :key="m" class="c-occ">
        <OccCell
          :allocated="allocated[m]"
          :capacity="c"
          :month="m"
          :label="resource.name"
          :mode="mode"
          :thresholds="thresholds"
          :absence="absenceBy.get(m)?.reason || ''"
          :current="m === currentMonth"
        />
      </div>
      <div class="c-year">
        <OccCell :allocated="yearAllocated" :capacity="yearCapacity" :label="`${resource.name}, helår`" :mode="mode" :thresholds="thresholds" />
      </div>
    </div>

    <Expander :id="regionId" :open="open" role="region" :aria-label="`Uppdrag för ${resource.name}`">
      <div
        v-for="a in rows"
        :key="a.id"
        class="g-row asg-row"
        :class="{ flash: a.id === flashAllocId, active: a.id === activeAllocId }"
        :data-alloc="a.id"
      >
        <div class="c-name">
          <span class="indent" />
          <div class="asg">
            <button class="asg-name ellipsis" :title="asg(a)?.name" @click="emit('edit-assignment', asg(a))">
              {{ asg(a)?.name }}
            </button>
            <span class="asg-meta">
              <span v-if="obj(a)" class="chip dot ellipsis" :style="{ '--chip-color': obj(a).color }">{{ obj(a).name }}</span>
              <span
                class="chip kvar"
                :class="kvarClass(kvar(a))"
                :title="`Kvar att fördela på uppdraget (estimat ${fmt(asg(a)?.estimatedHours)} h)`"
              >
                {{ kvar(a) === 0 ? '✓' : kvar(a) > 0 ? `kvar ${fmt(kvar(a))}` : `+${fmt(-kvar(a))} över` }}
              </span>
              <span v-if="a.note" class="chip" :title="a.note">💬</span>
            </span>
          </div>
          <button
            v-if="editable"
            class="btn ghost icon sm row-menu"
            :aria-label="`Åtgärder för ${asg(a)?.name}`"
            @click="emit('row-menu', { allocation: a, anchor: $event.currentTarget })"
          >
            <Icon name="more" :size="16" />
          </button>
        </div>
        <div v-for="m in 12" :key="m" class="c-cell" :class="{ current: m - 1 === currentMonth }">
          <input
            v-if="editable"
            class="hc num"
            inputmode="numeric"
            autocomplete="off"
            :data-col="m - 1"
            :value="a.hours[m - 1] || ''"
            :aria-label="cellLabel(a, m - 1)"
            @focus="onFocus(a, m - 1, $event)"
            @blur="onBlur(a, m - 1, $event)"
            @keydown="onKey(a, m - 1, $event)"
            @paste="onPaste(a, m - 1, $event)"
          />
          <span v-else class="ro num">{{ a.hours[m - 1] || '' }}</span>
        </div>
        <div class="c-year num strong">{{ fmt(sum(a.hours)) }}</div>
      </div>

      <div v-if="!rows.length" class="g-row empty-row">
        <div class="c-name span-all">
          <span class="indent" />
          <span class="faint small">Inga uppdrag. {{ editable ? 'Dra hit från backloggen eller använd “Lägg till uppdrag”.' : '' }}</span>
        </div>
      </div>

      <div class="g-row cap-row">
        <div class="c-name">
          <span class="indent" />
          <span class="muted small">Kapacitet <span class="faint">(h)</span></span>
        </div>
        <div v-for="(c, m) in capacity" :key="m" class="c-cell">
          <button
            class="cap-cell num"
            :class="{ abs: absenceBy.has(m) }"
            :disabled="!editable"
            :title="absenceBy.get(m) ? `${absenceBy.get(m).reason || 'Avvikande kapacitet'}: ${c} h` : editable ? 'Klicka för att ange frånvaro/avvikande kapacitet' : ''"
            :aria-label="`Kapacitet ${MONTH_NAMES[m]}: ${c} timmar${absenceBy.get(m) ? `, ${absenceBy.get(m).reason || 'avvikelse'}` : ''}`"
            @click="emit('absence', m)"
          >
            {{ c }}<span v-if="absenceBy.has(m)" class="abs-dot" aria-hidden="true" />
          </button>
        </div>
        <div class="c-year num muted">{{ fmt(yearCapacity) }}</div>
      </div>
      <div v-if="editable" class="g-row add-row">
        <div class="c-name">
          <span class="indent" />
          <button class="btn ghost sm add-btn" @click="emit('add')"><Icon name="plus" :size="14" /> Lägg till uppdrag</button>
        </div>
      </div>
    </Expander>
  </div>
</template>

<style scoped>
.res {
  border-bottom: 1px solid var(--border);
}
.res-row {
  position: relative;
  min-height: 44px;
  background: var(--surface);
}
.res-row > .c-occ,
.res-row > .c-year {
  padding: 4px 2px;
}
.res-toggle {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  flex: 1;
  border: 0;
  background: transparent;
  color: var(--text);
  font: inherit;
  text-align: left;
  padding: 4px 4px 4px 0;
  cursor: pointer;
  border-radius: var(--r-sm);
}
.res-toggle:hover strong {
  color: var(--accent);
}
.avatar {
  flex: none;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: var(--fs-xs);
  font-weight: 700;
  color: var(--c, var(--accent));
  background: color-mix(in srgb, var(--c, var(--accent)) 15%, var(--surface));
  border: 1px solid color-mix(in srgb, var(--c, var(--accent)) 35%, transparent);
}
@media (max-width: 767px) {
  .avatar {
    display: none;
  }
}
.nm {
  display: flex;
  flex-direction: column;
  min-width: 0;
  line-height: 1.25;
}
.nm strong {
  font-weight: 600;
  transition: color var(--dur-1);
}
.sub {
  font-size: var(--fs-xs);
  color: var(--text-muted);
}

.asg-row,
.cap-row,
.add-row,
.empty-row {
  background: var(--surface-2);
  min-height: 40px;
}
.asg-row .c-name,
.cap-row .c-name,
.add-row .c-name,
.empty-row .c-name {
  background: var(--surface-2);
}
.asg-row.active .c-name {
  box-shadow: inset 3px 0 0 var(--accent);
}
.indent {
  flex: none;
  width: 22px;
  align-self: stretch;
  border-right: 2px solid var(--accent-soft);
  margin-right: 8px;
}
.asg {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
  gap: 2px;
  padding: 4px 0;
}
.asg-name {
  border: 0;
  background: none;
  padding: 0;
  font: inherit;
  font-size: var(--fs-table);
  font-weight: 550;
  color: var(--text);
  text-align: left;
  cursor: pointer;
}
.asg-name:hover {
  color: var(--accent);
  text-decoration: underline;
}
.asg-meta {
  display: flex;
  gap: 4px;
  min-width: 0;
}
.asg-meta .chip {
  height: 18px;
  font-size: 10px;
  padding: 0 6px;
}
.asg-meta .chip.dot {
  max-width: 110px;
}
.row-menu {
  opacity: 0.6;
}
.asg-row:hover .row-menu,
.row-menu:focus-visible {
  opacity: 1;
}

.c-cell {
  padding: 3px 2px;
  display: flex;
}
.c-cell.current {
  background: color-mix(in srgb, var(--accent) 6%, transparent);
}
.hc {
  width: 100%;
  min-width: 0;
  height: 32px;
  border: 1px solid transparent;
  border-radius: var(--r-sm);
  background: var(--surface);
  color: var(--text);
  font: inherit;
  font-size: var(--fs-table);
  padding: 0 8px;
  text-align: right;
  transition:
    border-color var(--dur-1),
    box-shadow var(--dur-1);
}
.hc:hover {
  border-color: var(--border);
}
.hc:focus {
  outline: none;
  border-color: var(--accent);
  box-shadow: inset 0 0 0 1px var(--accent);
}
.hc.invalid {
  border-bottom: 2px solid var(--danger);
}
.ro {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  padding: 0 8px;
  font-size: var(--fs-table);
}
.c-year.strong {
  font-weight: 650;
  font-size: var(--fs-table);
  display: flex;
  align-items: center;
  justify-content: flex-end;
  padding-right: 10px;
}
.cap-cell {
  position: relative;
  width: 100%;
  height: 28px;
  border: 1px dashed transparent;
  border-radius: var(--r-sm);
  background: transparent;
  color: var(--text-muted);
  font: inherit;
  font-size: var(--fs-sm);
  padding: 0 8px;
  text-align: right;
  cursor: pointer;
}
.cap-cell:not(:disabled):hover {
  border-color: var(--border-strong);
  color: var(--text);
}
.cap-cell:disabled {
  cursor: default;
}
.cap-cell.abs {
  color: var(--occ-near-fg);
  font-weight: 650;
}
.abs-dot {
  position: absolute;
  top: 3px;
  left: 4px;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--occ-near-bar);
}
.cap-row .c-year {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  padding-right: 10px;
  font-size: var(--fs-sm);
}
.add-btn {
  color: var(--accent);
}
.span-all {
  grid-column: 1 / -1;
}
</style>

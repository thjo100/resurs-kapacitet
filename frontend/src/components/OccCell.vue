<script setup>
// Beläggningscell (heatmap): timmar/procent, bakgrundston efter nivå, liten stapel i botten.
import { computed } from 'vue'
import { occLevel, pct, OCC_LABELS, MONTH_NAMES } from '../utils/calc.js'
import { fmt } from '../utils/format.js'

const props = defineProps({
  allocated: { type: Number, default: 0 },
  capacity: { type: Number, default: 0 },
  month: { type: Number, default: null }, // 0–11
  label: { type: String, default: '' },
  mode: { type: String, default: 'hours' }, // hours | percent | free
  thresholds: { type: Object, default: undefined },
  absence: { type: String, default: '' },
  current: { type: Boolean, default: false },
  tag: { type: String, default: 'div' },
})

const level = computed(() => occLevel(props.allocated, props.capacity, props.thresholds))
const p = computed(() => pct(props.allocated, props.capacity))
const free = computed(() => props.capacity - props.allocated)
const width = computed(() => (Number.isFinite(p.value) ? Math.min(100, p.value) : 100))

const text = computed(() => {
  if (props.mode === 'percent') return props.allocated ? (Number.isFinite(p.value) ? `${Math.round(p.value)}%` : '∞') : '–'
  if (props.mode === 'free') return props.capacity || props.allocated ? fmt(free.value) : '–'
  return props.allocated ? fmt(props.allocated) : '–'
})

const title = computed(() => {
  const m = props.month !== null ? MONTH_NAMES[props.month] : ''
  const head = [props.label, m].filter(Boolean).join(', ')
  const pctText = Number.isFinite(p.value) ? `${Math.round(p.value)} %` : 'ingen kapacitet'
  const freeText = free.value >= 0 ? `${fmt(free.value)} h ledigt` : `${fmt(-free.value)} h för mycket`
  return `${head}: ${fmt(props.allocated)} av ${fmt(props.capacity)} h (${pctText}), ${freeText} – ${OCC_LABELS[level.value]}${props.absence ? ` · ${props.absence}` : ''}`
})
</script>

<template>
  <component
    :is="tag"
    class="occ"
    :class="[`lv-${level}`, { zero: !capacity, current }]"
    :title="title"
    :aria-label="title"
    role="img"
  >
    <span class="v num">{{ text }}</span>
    <span v-if="level === 'over' || level === 'near'" class="tri" aria-hidden="true">▲</span>
    <span class="bar" aria-hidden="true"><span :style="{ width: width + '%' }" /></span>
  </component>
</template>

<style scoped>
.occ {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  height: 100%;
  min-height: 30px;
  padding: 0 8px 3px;
  font-size: var(--fs-table);
  font-weight: 500;
  border-radius: var(--r-sm);
  transition:
    background-color var(--dur-2),
    color var(--dur-2);
}
.occ.current {
  box-shadow: inset 0 0 0 1px var(--accent-soft);
}
.v {
  position: relative;
}
.bar {
  position: absolute;
  left: 4px;
  right: 4px;
  bottom: 3px;
  height: 3px;
  border-radius: 2px;
  background: color-mix(in srgb, var(--bar) 20%, transparent);
  overflow: hidden;
}
.bar > span {
  display: block;
  height: 100%;
  background: var(--bar);
  border-radius: 2px;
  transition: width var(--dur-2) var(--ease-out);
}
.tri {
  position: absolute;
  top: 2px;
  right: 3px;
  font-size: 6px;
  line-height: 1;
}
.lv-empty {
  color: var(--occ-empty-fg);
  --bar: transparent;
}
.lv-low {
  background: var(--occ-low-bg);
  color: var(--occ-low-fg);
  --bar: var(--occ-low-bar);
}
.lv-ok {
  background: var(--occ-ok-bg);
  color: var(--occ-ok-fg);
  --bar: var(--occ-ok-bar);
}
.lv-near {
  background: var(--occ-near-bg);
  color: var(--occ-near-fg);
  --bar: var(--occ-near-bar);
  font-weight: 600;
}
.lv-over {
  background: var(--occ-over-bg);
  color: var(--occ-over-fg);
  --bar: var(--occ-over-bar);
  font-weight: 600;
}
.zero:not(.lv-over) {
  background-image: repeating-linear-gradient(135deg, transparent 0 4px, var(--hatch) 4px 5px);
}
</style>

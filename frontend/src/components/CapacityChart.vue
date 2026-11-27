<script setup>
// Kolumndiagram: planerade timmar per månad (staplar) mot kapacitet (markering per månad).
import { computed, ref } from 'vue'
import { MONTHS, MONTH_NAMES, pct } from '../utils/calc.js'
import { fmt, fmtPct } from '../utils/format.js'

const props = defineProps({
  allocated: { type: Array, required: true },
  capacity: { type: Array, required: true },
  currentMonth: { type: Number, default: -1 },
})

const W = 720
const H = 240
const pad = { l: 44, r: 12, t: 18, b: 28 }
const iw = W - pad.l - pad.r
const ih = H - pad.t - pad.b
const band = iw / 12
const barW = Math.min(24, band * 0.5)

const max = computed(() => {
  const m = Math.max(1, ...props.allocated, ...props.capacity)
  const step = niceStep(m / 4)
  return Math.ceil(m / step) * step
})
function niceStep(x) {
  const p = Math.pow(10, Math.floor(Math.log10(x)))
  const n = x / p
  return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * p
}
const ticks = computed(() => {
  const step = max.value / 4
  return [0, 1, 2, 3, 4].map((i) => Math.round(i * step))
})
const y = (v) => pad.t + ih - (v / max.value) * ih
const x = (m) => pad.l + band * m + band / 2

function barPath(m) {
  const v = props.allocated[m]
  if (!v) return ''
  const top = y(v)
  const h = pad.t + ih - top
  const r = Math.min(4, h)
  const l = x(m) - barW / 2
  const rr = l + barW
  const b = pad.t + ih
  return `M${l},${b} L${l},${top + r} Q${l},${top} ${l + r},${top} L${rr - r},${top} Q${rr},${top} ${rr},${top + r} L${rr},${b} Z`
}

const hover = ref(-1)
const tip = computed(() => {
  const m = hover.value
  if (m < 0) return null
  const a = props.allocated[m]
  const c = props.capacity[m]
  return { m, a, c, p: pct(a, c), free: c - a, left: (x(m) / W) * 100 }
})
const fmtTick = (v) => (v >= 10000 ? `${Math.round(v / 1000)}k` : fmt(v))
</script>

<template>
  <div class="chart">
    <div class="legend" aria-hidden="true">
      <span><i class="sw bar" /> Planerat</span>
      <span><i class="sw cap" /> Kapacitet</span>
      <span class="faint">▲ = överbelagd månad</span>
    </div>
    <div class="plot" @mouseleave="hover = -1">
      <svg :viewBox="`0 0 ${W} ${H}`" role="img" :aria-label="`Planerat och kapacitet per månad`">
        <g class="grid">
          <line v-for="t in ticks" :key="t" :x1="pad.l" :x2="W - pad.r" :y1="y(t)" :y2="y(t)" />
        </g>
        <g class="yaxis">
          <text v-for="t in ticks" :key="t" :x="pad.l - 8" :y="y(t) + 4" text-anchor="end">{{ fmtTick(t) }}</text>
        </g>
        <g v-for="(mn, m) in MONTHS" :key="mn">
          <rect
            class="hit"
            :x="pad.l + band * m"
            :y="pad.t"
            :width="band"
            :height="ih"
            :class="{ on: hover === m }"
            @mouseenter="hover = m"
            @focus="hover = m"
            @blur="hover = -1"
            tabindex="0"
            :aria-label="`${MONTH_NAMES[m]}: planerat ${allocated[m]} h, kapacitet ${capacity[m]} h`"
          />
          <path class="bar" :class="{ dim: hover >= 0 && hover !== m }" :d="barPath(m)" />
          <line class="cap" :x1="x(m) - barW / 2 - 6" :x2="x(m) + barW / 2 + 6" :y1="y(capacity[m])" :y2="y(capacity[m])" />
          <text v-if="allocated[m] > capacity[m] + 0.5" class="over" :x="x(m)" :y="Math.min(y(allocated[m]), y(capacity[m])) - 6" text-anchor="middle">▲</text>
          <text class="xlab" :class="{ current: m === currentMonth }" :x="x(m)" :y="H - 8" text-anchor="middle">{{ mn }}</text>
        </g>
        <line class="base" :x1="pad.l" :x2="W - pad.r" :y1="pad.t + ih" :y2="pad.t + ih" />
      </svg>
      <div v-if="tip" class="tooltip" :style="{ left: tip.left + '%' }" role="status">
        <strong>{{ MONTH_NAMES[tip.m] }}</strong>
        <div class="tr"><i class="sw bar" /> Planerat <span class="num">{{ fmt(tip.a) }} h</span></div>
        <div class="tr"><i class="sw cap" /> Kapacitet <span class="num">{{ fmt(tip.c) }} h</span></div>
        <div class="tr muted">
          Beläggning <span class="num">{{ fmtPct(tip.p) }}</span>
        </div>
        <div class="tr" :class="tip.free < 0 ? 'danger-text' : 'muted'">
          {{ tip.free >= 0 ? 'Ledigt' : 'För mycket' }} <span class="num">{{ fmt(Math.abs(tip.free)) }} h</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.chart {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.legend {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  font-size: var(--fs-sm);
  color: var(--text-muted);
}
.legend span {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.sw {
  display: inline-block;
  width: 12px;
  height: 12px;
  border-radius: 3px;
}
.sw.bar {
  background: var(--accent);
}
.sw.cap {
  height: 2px;
  border-radius: 1px;
  background: var(--text-muted);
}
.plot {
  position: relative;
}
svg {
  width: 100%;
  height: auto;
  min-height: 160px;
  display: block;
  overflow: visible;
}
.grid line {
  stroke: var(--border);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}
.base {
  stroke: var(--border-strong);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}
.yaxis text,
.xlab {
  fill: var(--text-muted);
  font-size: 11px;
  font-variant-numeric: tabular-nums;
}
.xlab.current {
  fill: var(--accent);
  font-weight: 700;
}
.bar {
  fill: var(--accent);
  transition: opacity var(--dur-2);
}
.bar.dim {
  opacity: 0.45;
}
.cap {
  stroke: var(--text-muted);
  stroke-width: 2;
  stroke-linecap: round;
  vector-effect: non-scaling-stroke;
}
.over {
  fill: var(--danger);
  font-size: 10px;
}
.hit {
  fill: transparent;
  cursor: default;
  outline: none;
}
.hit.on {
  fill: var(--surface-2);
}
.tooltip {
  position: absolute;
  top: 0;
  transform: translateX(-50%);
  min-width: 170px;
  padding: 8px 10px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  box-shadow: var(--sh-2);
  font-size: var(--fs-sm);
  pointer-events: none;
  z-index: 5;
}
.tooltip strong {
  text-transform: capitalize;
}
.tr {
  display: flex;
  align-items: center;
  gap: 6px;
}
.tr .num {
  margin-left: auto;
  font-weight: 600;
}
</style>

<script setup>
// Backlogg: prioritera med drag-n-drop och dra uppdrag till resurser i planeringen.
import { computed, ref } from 'vue'
import { store, canEdit, reorderBacklog, assignmentRemaining, toastError, toast } from '../store.js'
import { vSortableList } from '../utils/dnd.js'
import { fmt, STATUS } from '../utils/format.js'
import { sum } from '../utils/calc.js'
import BacklogCard from './BacklogCard.vue'
import Expander from './Expander.vue'
import Icon from './Icon.vue'

defineProps({
  compact: { type: Boolean, default: false },
})
const emit = defineEmits(['assign', 'edit', 'create'])

const q = ref('')
const showOther = ref(true)
const match = (a) => !q.value || a.name.toLowerCase().includes(q.value.toLowerCase())

const backlog = computed(() =>
  (store.planning?.assignments ?? []).filter((a) => a.status === 'Backlogg').sort((x, y) => x.priority - y.priority),
)
const visibleBacklog = computed(() => backlog.value.filter(match))
// Filter för övriga uppdrag: välj vilka statusar som visas (Pausad ska gå att hitta) och om fullt fördelade ska med.
const otherStatuses = ['Planerad', 'Pagaende', 'Pausad', 'Klar']
const shownStatuses = ref(new Set(['Planerad', 'Pagaende', 'Pausad']))
const showFull = ref(false)
function toggleStatus(st) {
  const s = new Set(shownStatuses.value)
  s.has(st) ? s.delete(st) : s.add(st)
  shownStatuses.value = s
}
const other = computed(() =>
  (store.planning?.assignments ?? [])
    .filter((a) => a.status !== 'Backlogg' && shownStatuses.value.has(a.status))
    .filter((a) => showFull.value || a.status === 'Klar' || assignmentRemaining(a) > 0)
    .filter(match)
    .sort((x, y) => x.priority - y.priority),
)
const backlogHours = computed(() => sum(backlog.value.map((a) => Math.max(0, assignmentRemaining(a)))))

async function onReorder(visibleIds) {
  // Vid filtrering: behåll dolda posters relativa placering.
  const all = backlog.value.map((a) => a.id)
  const visibleSet = new Set(visibleIds)
  const queue = [...visibleIds]
  const ids = all.map((id) => (visibleSet.has(id) ? queue.shift() : id))
  try {
    await reorderBacklog(ids)
    toast('Backloggen omprioriterades.', { type: 'success', timeout: 2000 })
  } catch (e) {
    toastError(e)
  }
}
</script>

<template>
  <section class="backlog" :class="{ compact }" aria-label="Backlogg">
    <header class="bp-head">
      <div>
        <h3>Backlogg</h3>
        <p class="muted small">{{ backlog.length }} uppdrag · {{ fmt(backlogHours) }} h att fördela</p>
      </div>
      <span class="spacer" />
      <button v-if="canEdit" class="btn sm primary" @click="emit('create')"><Icon name="plus" :size="14" /> Nytt</button>
    </header>

    <div class="search">
      <Icon name="search" :size="14" />
      <input v-model="q" class="input" type="search" placeholder="Filtrera uppdrag…" aria-label="Filtrera backloggen" />
    </div>

    <p v-if="canEdit && backlog.length" class="hint faint xs">
      Dra i <Icon name="grip" :size="12" /> för att prioritera, eller släpp på en resurs för att tilldela.
    </p>

    <div v-if="!visibleBacklog.length" class="empty small">
      <Icon name="inbox" :size="32" class="icon" />
      <strong>{{ q ? 'Inga träffar' : 'Backloggen är tom' }}</strong>
      <span>{{ q ? 'Prova ett annat sökord.' : 'Alla uppdrag är tilldelade. Skapa ett nytt med “Nytt”.' }}</span>
    </div>
    <div
      v-else
      v-sortable-list="{ sort: canEdit, disabled: !canEdit, onReorder }"
      class="list"
      aria-label="Backloggposter i prioritetsordning"
    >
      <BacklogCard
        v-for="a in visibleBacklog"
        :key="a.id"
        :assignment="a"
        :rank="backlog.indexOf(a) + 1"
        @assign="emit('assign', $event)"
        @edit="emit('edit', $event)"
      />
    </div>

    <button class="section-toggle" :aria-expanded="showOther" @click="showOther = !showOther">
      <Icon name="chevron-right" :size="14" class="chev" />
      Övriga uppdrag <span class="chip">{{ other.length }}</span>
    </button>
    <Expander :open="showOther">
      <div class="filters" role="group" aria-label="Filtrera på status">
        <button
          v-for="st in otherStatuses"
          :key="st"
          class="fchip"
          :class="{ on: shownStatuses.has(st) }"
          :aria-pressed="shownStatuses.has(st)"
          @click="toggleStatus(st)"
        >
          {{ STATUS[st] }}
        </button>
        <label class="checkbox xs"><input v-model="showFull" type="checkbox" /> Visa även fullt fördelade</label>
      </div>
      <div v-if="other.length" v-sortable-list="{ sort: false, disabled: !canEdit }" class="list">
        <BacklogCard
          v-for="a in other"
          :key="a.id"
          :assignment="a"
          show-status
          @assign="emit('assign', $event)"
          @edit="emit('edit', $event)"
        />
      </div>
      <p v-else class="faint small" style="padding: 4px 2px 8px">
        Inga uppdrag matchar filtret. Slå på fler statusar eller “Visa även fullt fördelade”.
      </p>
    </Expander>
  </section>
</template>

<style scoped>
.filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  padding: 2px 2px 10px;
}
.fchip {
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text-muted);
  font: inherit;
  font-size: var(--fs-xs);
  font-weight: 600;
  padding: 3px 10px;
  border-radius: var(--r-full);
  cursor: pointer;
}
.fchip:hover {
  background: var(--surface-2);
}
.fchip.on {
  background: var(--accent-soft);
  border-color: var(--accent);
  color: var(--accent);
}
.backlog {
  display: flex;
  flex-direction: column;
  gap: var(--sp-2);
  min-height: 0;
}
.bp-head {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
}
.search {
  position: relative;
  display: flex;
  align-items: center;
}
.search > .icon {
  position: absolute;
  left: 10px;
  color: var(--text-faint);
  pointer-events: none;
}
.search .input {
  padding-left: 30px;
}
.hint {
  display: block;
}
.hint :deep(.icon) {
  vertical-align: -2px;
}
.list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 2px;
}
.section-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  border: 0;
  background: transparent;
  color: var(--text-muted);
  font: inherit;
  font-weight: 650;
  font-size: var(--fs-sm);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 10px 2px 4px;
  cursor: pointer;
}
.section-toggle:hover {
  color: var(--text);
}
</style>

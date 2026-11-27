<script setup>
import { computed } from 'vue'
import { index, assignmentAllocatedTotal, canEdit } from '../store.js'
import { fmt, STATUS } from '../utils/format.js'
import Icon from './Icon.vue'

const props = defineProps({
  assignment: { type: Object, required: true },
  rank: { type: Number, default: null },
  showStatus: { type: Boolean, default: false },
  draggable: { type: Boolean, default: true },
})
const emit = defineEmits(['assign', 'edit'])

const obj = computed(() => (props.assignment.objectId ? index.value?.objects.get(props.assignment.objectId) : null))
const allocated = computed(() => assignmentAllocatedTotal(props.assignment))
const remaining = computed(() => props.assignment.estimatedHours - allocated.value)
const progress = computed(() =>
  props.assignment.estimatedHours ? Math.min(100, (allocated.value / props.assignment.estimatedHours) * 100) : 0,
)
const state = computed(() => (remaining.value < 0 ? 'over' : remaining.value === 0 ? 'done' : ''))
const kvarClass = computed(() => (remaining.value < 0 ? 'neg' : remaining.value === 0 ? 'zero' : 'pos'))
</script>

<template>
  <article class="bl-card" :data-id="assignment.id" :aria-label="`${assignment.name}, ${fmt(remaining)} timmar kvar att fördela`">
    <button
      v-if="canEdit && draggable"
      class="grip"
      type="button"
      :aria-label="`Dra ${assignment.name}`"
      title="Dra för att prioritera eller släpp på en resurs"
    >
      <Icon name="grip" :size="16" />
    </button>
    <div class="body">
      <div class="top">
        <span v-if="rank" class="rank num">{{ rank }}</span>
        <button class="name" type="button" :title="assignment.description || assignment.name" @click="emit('edit', assignment)">
          {{ assignment.name }}
        </button>
      </div>
      <div class="meta">
        <span v-if="obj" class="chip dot" :style="{ '--chip-color': obj.color }">{{ obj.name }}</span>
        <span v-if="showStatus" class="badge" :class="`st-${assignment.status}`">{{ STATUS[assignment.status] }}</span>
        <span class="spacer" />
        <span class="chip kvar" :class="kvarClass" :title="`Estimat ${fmt(assignment.estimatedHours)} h, fördelat ${fmt(allocated)} h`">
          <template v-if="remaining > 0">{{ fmt(remaining) }} h kvar</template>
          <template v-else-if="remaining === 0">✓ fördelat</template>
          <template v-else>{{ fmt(-remaining) }} h över</template>
        </span>
      </div>
      <div class="progress thin" :class="state" :title="`${fmt(allocated)} av ${fmt(assignment.estimatedHours)} h`">
        <div class="fill" :style="{ width: progress + '%' }" />
      </div>
    </div>
    <button v-if="canEdit" class="btn sm assign no-drag" type="button" @click="emit('assign', assignment)">
      Tilldela
    </button>
  </article>
</template>

<style scoped>
.bl-card {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 8px 8px 4px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  box-shadow: var(--sh-1);
  transition:
    border-color var(--dur-1),
    box-shadow var(--dur-1);
}
.bl-card:hover {
  border-color: var(--border-strong);
}
.grip {
  flex: none;
  display: grid;
  place-items: center;
  width: 24px;
  height: 36px;
  border: 0;
  background: transparent;
  color: var(--text-faint);
  border-radius: var(--r-sm);
  cursor: grab;
  touch-action: none;
}
.grip:hover {
  color: var(--text);
  background: var(--surface-2);
}
@media (pointer: coarse) {
  .grip {
    width: 36px;
    height: 44px;
  }
}
.body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding-left: 2px;
}
.top {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.rank {
  flex: none;
  min-width: 20px;
  height: 20px;
  padding: 0 5px;
  border-radius: var(--r-sm);
  background: var(--surface-2);
  color: var(--text-muted);
  font-size: var(--fs-xs);
  font-weight: 700;
  display: grid;
  place-items: center;
}
.name {
  border: 0;
  background: none;
  padding: 0;
  font: inherit;
  font-weight: 600;
  color: var(--text);
  text-align: left;
  cursor: pointer;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}
.name:hover {
  color: var(--accent);
  text-decoration: underline;
}
.meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px 6px;
  min-width: 0;
}
.meta .chip:first-child {
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 55%;
}
.assign {
  flex: none;
}
</style>

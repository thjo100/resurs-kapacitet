<script setup>
import { computed, reactive } from 'vue'
import { api } from '../api.js'
import { store, loadPlanning, toast, toastError, assignmentAllocatedTotal } from '../store.js'
import { STATUS, STATUS_ORDER, fmt } from '../utils/format.js'
import { confirmDialog } from './dialogs.js'
import Modal from './Modal.vue'

const props = defineProps({
  assignment: { type: Object, default: null },
  defaults: { type: Object, default: () => ({}) },
})
const emit = defineEmits(['close', 'saved'])

const isNew = computed(() => !props.assignment?.id)
const a = props.assignment ?? {}
const form = reactive({
  name: a.name ?? props.defaults.name ?? '',
  objectId: a.objectId ?? props.defaults.objectId ?? null,
  estimatedHours: a.estimatedHours ?? props.defaults.estimatedHours ?? 0,
  status: a.status ?? props.defaults.status ?? 'Backlogg',
  startDate: a.startDate ?? null,
  endDate: a.endDate ?? null,
  requester: a.requester ?? '',
  responsible: a.responsible ?? '',
  description: a.description ?? '',
})
const busy = reactive({ saving: false })
const objects = computed(() => (store.planning?.objects ?? []).filter((o) => o.isActive || o.id === form.objectId))
const allocated = computed(() => (props.assignment?.id ? assignmentAllocatedTotal(props.assignment) : 0))

async function save() {
  if (!form.name.trim()) return toast('Ange ett namn.', { type: 'error' })
  busy.saving = true
  try {
    const body = {
      ...form,
      estimatedHours: Number(form.estimatedHours) || 0,
      startDate: form.startDate || null,
      endDate: form.endDate || null,
    }
    const saved = isNew.value
      ? await api.post('/api/assignments', body)
      : await api.put(`/api/assignments/${props.assignment.id}`, body)
    await loadPlanning(store.planningYear ?? store.year, { silent: true })
    toast(isNew.value ? `Uppdraget ${saved.name} skapades.` : 'Uppdraget sparades.', { type: 'success' })
    emit('saved', saved)
    emit('close')
  } catch (e) {
    toastError(e)
  } finally {
    busy.saving = false
  }
}

async function remove() {
  const ok = await confirmDialog({
    title: 'Ta bort uppdrag?',
    message: `${props.assignment.name} tas bort${allocated.value ? ` tillsammans med alla fördelade timmar (${fmt(allocated.value)} h)` : ''}. Det går inte att ångra.`,
    confirmLabel: 'Ta bort',
    danger: true,
  })
  if (!ok) return
  try {
    await api.del(`/api/assignments/${props.assignment.id}`)
    await loadPlanning(store.planningYear ?? store.year, { silent: true })
    toast('Uppdraget togs bort.')
    emit('close')
  } catch (e) {
    toastError(e)
  }
}
</script>

<template>
  <Modal :title="isNew ? 'Nytt uppdrag' : 'Redigera uppdrag'" :subtitle="isNew ? '' : `Fördelat hittills: ${fmt(allocated)} h`" width="620px" @close="emit('close')">
    <form id="asg-form" class="form-grid" @submit.prevent="save">
      <label class="field full"><span>Namn *</span><input v-model="form.name" class="input" required autofocus maxlength="160" /></label>
      <label class="field">
        <span>Förvaltningsobjekt / projekt</span>
        <select v-model="form.objectId" class="input">
          <option :value="null">– Inget –</option>
          <option v-for="o in objects" :key="o.id" :value="o.id">{{ o.name }}</option>
        </select>
      </label>
      <label class="field"><span>Estimat (timmar att fördela)</span><input v-model.number="form.estimatedHours" class="input num" type="number" min="0" step="1" /></label>
      <label class="field">
        <span>Status</span>
        <select v-model="form.status" class="input">
          <option v-for="s in STATUS_ORDER" :key="s" :value="s">{{ STATUS[s] }}</option>
        </select>
      </label>
      <label class="field"><span>Beställare</span><input v-model="form.requester" class="input" /></label>
      <label class="field"><span>Ansvarig</span><input v-model="form.responsible" class="input" /></label>
      <label class="field"><span>Start</span><input v-model="form.startDate" class="input" type="date" /></label>
      <label class="field"><span>Slut</span><input v-model="form.endDate" class="input" type="date" /></label>
      <label class="field full"><span>Beskrivning</span><textarea v-model="form.description" class="input" rows="3" /></label>
    </form>
    <template #footer>
      <button v-if="!isNew" class="btn danger" type="button" @click="remove">Ta bort</button>
      <span class="spacer" />
      <button class="btn" type="button" @click="emit('close')">Avbryt</button>
      <button class="btn primary" type="submit" form="asg-form" :disabled="busy.saving">{{ isNew ? 'Skapa' : 'Spara' }}</button>
    </template>
  </Modal>
</template>

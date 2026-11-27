<script setup>
// Generisk admin-tabell med sök, skapa, redigera (panel från höger) och ta bort.
import { computed, reactive, ref } from 'vue'
import { api } from '../api.js'
import { toast, toastError } from '../store.js'
import { confirmDialog } from './dialogs.js'
import Modal from './Modal.vue'
import Icon from './Icon.vue'

const props = defineProps({
  title: { type: String, required: true },
  singular: { type: String, required: true },
  items: { type: Array, required: true },
  columns: { type: Array, required: true },
  fields: { type: Array, default: () => [] },
  endpoint: { type: String, required: true },
  editable: { type: Boolean, default: true },
  newDefaults: { type: Object, default: () => ({}) },
  deleteMessage: { type: Function, default: null },
  customEdit: { type: Function, default: null },
  description: { type: String, default: '' },
})
const emit = defineEmits(['changed'])

const q = ref('')
const filtered = computed(() => {
  const t = q.value.trim().toLowerCase()
  if (!t) return props.items
  return props.items.filter((it) => props.columns.some((c) => String(c.format ? c.format(it) : it[c.key] ?? '').toLowerCase().includes(t)))
})

const form = reactive({ open: false, item: null, data: {}, busy: false })
function openForm(item = null) {
  if (props.customEdit) return props.customEdit(item)
  const data = {}
  for (const f of props.fields) data[f.key] = item ? (item[f.key] ?? f.empty ?? null) : (props.newDefaults[f.key] ?? f.default ?? (f.type === 'checkbox' ? false : f.empty ?? null))
  Object.assign(form, { open: true, item, data, busy: false })
}
const visibleFields = computed(() => props.fields.filter((f) => !f.show || f.show(form.item, form.data)))

async function save() {
  form.busy = true
  try {
    const body = { ...form.data }
    for (const f of props.fields) {
      if (f.type === 'number') body[f.key] = body[f.key] === '' || body[f.key] === null ? null : Number(body[f.key])
      if (f.type === 'date' && !body[f.key]) body[f.key] = null
    }
    if (form.item) await api.put(`${props.endpoint}/${form.item.id}`, body)
    else await api.post(props.endpoint, body)
    toast(`${props.singular} ${form.item ? 'sparad' : 'skapad'}.`, { type: 'success' })
    form.open = false
    emit('changed')
  } catch (e) {
    toastError(e)
  } finally {
    form.busy = false
  }
}

async function remove(item) {
  const ok = await confirmDialog({
    title: `Ta bort ${props.singular.toLowerCase()}?`,
    message: props.deleteMessage ? props.deleteMessage(item) : `${item.name ?? item.displayName} tas bort.`,
    confirmLabel: 'Ta bort',
    danger: true,
  })
  if (!ok) return
  try {
    await api.del(`${props.endpoint}/${item.id}`)
    toast(`${props.singular} borttagen.`)
    form.open = false
    emit('changed')
  } catch (e) {
    toastError(e)
  }
}
</script>

<template>
  <section class="card crud">
    <header class="card-head">
      <div>
        <h3>{{ title }} <span class="chip">{{ items.length }}</span></h3>
        <p v-if="description" class="muted small">{{ description }}</p>
      </div>
      <span class="spacer" />
      <div class="search">
        <Icon name="search" :size="14" />
        <input v-model="q" class="input" type="search" :placeholder="`Sök…`" :aria-label="`Sök ${title.toLowerCase()}`" />
      </div>
      <button v-if="editable" class="btn primary sm" @click="openForm()"><Icon name="plus" :size="14" /> Ny</button>
    </header>
    <div class="table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th v-for="c in columns" :key="c.key" :class="c.class">{{ c.label }}</th>
            <th v-if="editable" class="actions"><span class="sr-only">Åtgärder</span></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="it in filtered" :key="it.id" :class="{ inactive: it.isActive === false }" @dblclick="editable && openForm(it)">
            <td v-for="c in columns" :key="c.key" :class="c.class">
              <slot :name="`cell-${c.key}`" :item="it">{{ c.format ? c.format(it) : it[c.key] }}</slot>
            </td>
            <td v-if="editable" class="actions">
              <button class="btn ghost icon sm" :aria-label="`Redigera ${it.name ?? it.displayName}`" @click="openForm(it)"><Icon name="edit" :size="15" /></button>
              <button class="btn ghost icon sm danger" :aria-label="`Ta bort ${it.name ?? it.displayName}`" @click="remove(it)"><Icon name="trash" :size="15" /></button>
            </td>
          </tr>
          <tr v-if="!filtered.length">
            <td :colspan="columns.length + 1" class="muted" style="text-align: center; padding: 24px">
              {{ items.length ? 'Inga träffar.' : `Inga ${title.toLowerCase()} ännu.` }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <Modal v-if="form.open" variant="drawer" width="480px" :title="form.item ? `Redigera ${singular.toLowerCase()}` : `Ny ${singular.toLowerCase()}`" :subtitle="form.item?.name ?? form.item?.displayName ?? ''" @close="form.open = false">
      <form id="crud-form" class="stack" @submit.prevent="save">
        <template v-for="f in visibleFields" :key="f.key">
          <label v-if="f.type === 'checkbox'" class="checkbox"><input v-model="form.data[f.key]" type="checkbox" /> {{ f.label }}</label>
          <label v-else class="field">
            <span>{{ f.label }}{{ f.required ? ' *' : '' }}</span>
            <select v-if="f.type === 'select'" v-model="form.data[f.key]" class="input">
              <option v-if="f.nullable" :value="null">{{ f.nullLabel ?? '– Inget –' }}</option>
              <option v-for="o in typeof f.options === 'function' ? f.options() : f.options" :key="o.value" :value="o.value">{{ o.label }}</option>
            </select>
            <textarea v-else-if="f.type === 'textarea'" v-model="form.data[f.key]" class="input" rows="3" />
            <span v-else-if="f.type === 'color'" class="color-row">
              <input v-model="form.data[f.key]" type="color" class="color" />
              <input v-model="form.data[f.key]" class="input" maxlength="16" />
            </span>
            <input
              v-else
              v-model="form.data[f.key]"
              class="input"
              :class="{ num: f.type === 'number' }"
              :type="f.type || 'text'"
              :required="f.required"
              :placeholder="f.placeholder"
              :min="f.min"
              :max="f.max"
              :autocomplete="f.type === 'password' ? 'new-password' : 'off'"
            />
            <small v-if="f.hint" class="faint">{{ f.hint }}</small>
          </label>
        </template>
      </form>
      <template #footer>
        <button v-if="form.item" class="btn danger" type="button" @click="remove(form.item)">Ta bort</button>
        <span class="spacer" />
        <button class="btn" type="button" @click="form.open = false">Avbryt</button>
        <button class="btn primary" type="submit" form="crud-form" :disabled="form.busy">Spara</button>
      </template>
    </Modal>
  </section>
</template>

<style scoped>
.crud h3 {
  display: flex;
  align-items: center;
  gap: 8px;
}
.search {
  position: relative;
  display: flex;
  align-items: center;
  width: 200px;
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
tr.inactive td {
  color: var(--text-faint);
}
.color-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.color {
  width: 44px;
  height: 34px;
  padding: 2px;
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  background: var(--surface);
}
.table-wrap {
  max-height: calc(100dvh - 260px);
}
@media (max-width: 767px) {
  .search {
    width: 100%;
    order: 5;
  }
  .card-head {
    flex-wrap: wrap;
  }
}
</style>

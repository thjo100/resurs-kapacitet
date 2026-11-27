// Drag-n-drop med SortableJS (pekar-/touchbaserad fallback så att det fungerar även på mobil).
import Sortable from 'sortablejs'

const COMMON = {
  animation: 200,
  easing: 'cubic-bezier(0.2, 0, 0, 1)',
  forceFallback: true,
  fallbackOnBody: true,
  fallbackTolerance: 4,
  delay: 300,
  delayOnTouchOnly: true,
  touchStartThreshold: 4,
  scroll: true,
  scrollSensitivity: 48,
  scrollSpeed: 12,
  bubbleScroll: true,
}

/**
 * Sorterbar lista med kort (.bl-card[data-id]). Kort kan även dras till drop-mål (resurser).
 * onReorder(ids) anropas med ny ordning. DOM återställs så att Vue äger renderingen.
 */
export function makeSortableList(el, { sort = true, onReorder, disabled = false } = {}) {
  return Sortable.create(el, {
    ...COMMON,
    group: { name: 'assign', pull: 'clone', put: false },
    sort,
    disabled,
    draggable: '.bl-card',
    handle: '.grip',
    filter: '.no-drag',
    preventOnFilter: false,
    onStart() {
      document.body.classList.add('dragging')
      navigator.vibrate?.(10)
    },
    onEnd(evt) {
      document.body.classList.remove('dragging')
      if (evt.from !== evt.to) return
      const o = evt.oldDraggableIndex
      const n = evt.newDraggableIndex
      if (o === n || o === undefined || n === undefined) return
      const ids = [...evt.from.querySelectorAll(':scope > .bl-card')].map((x) => Number(x.dataset.id))
      // Återställ DOM – Vue renderar om i ny ordning.
      evt.item.remove()
      evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex] ?? null)
      onReorder?.(ids)
    },
  })
}

/** Drop-mål (t.ex. en resursrad). onDrop(assignmentId) anropas när ett kort släpps. */
export function makeDropTarget(el, { onDrop, disabled = false }) {
  return Sortable.create(el, {
    ...COMMON,
    group: { name: 'assign', pull: false, put: true },
    sort: false,
    disabled,
    draggable: '.bl-card',
    onAdd(evt) {
      const id = Number(evt.item.dataset.id)
      // Lägg tillbaka originalkortet i listan och ta bort klonen.
      if (evt.clone?.parentNode) evt.clone.parentNode.replaceChild(evt.item, evt.clone)
      else evt.item.remove()
      document.body.classList.remove('dragging')
      onDrop?.(id)
    },
  })
}

// Vue-direktiv: v-sortable-list="{ sort, disabled, onReorder }" och v-drop-target="{ disabled, onDrop }"
export const vSortableList = {
  mounted(el, { value = {} }) {
    el._opts = value
    el._sortable = makeSortableList(el, {
      sort: value.sort !== false,
      disabled: !!value.disabled,
      onReorder: (ids) => el._opts.onReorder?.(ids),
    })
  },
  updated(el, { value = {} }) {
    el._opts = value
    el._sortable?.option('disabled', !!value.disabled)
  },
  unmounted(el) {
    el._sortable?.destroy()
  },
}

export const vDropTarget = {
  mounted(el, { value = {} }) {
    el._opts = value
    el._sortable = makeDropTarget(el, {
      disabled: !!value.disabled,
      onDrop: (id) => el._opts.onDrop?.(id),
    })
  },
  updated(el, { value = {} }) {
    el._opts = value
    el._sortable?.option('disabled', !!value.disabled)
  },
  unmounted(el) {
    el._sortable?.destroy()
  },
}

<script setup>
// Mjuk expand/collapse med CSS grid (0fr → 1fr). Innehållet renderas bara när det är öppet
// eller håller på att stängas, så stora listor kostar inget när de är ihopfällda.
import { ref, watch } from 'vue'

const props = defineProps({
  open: { type: Boolean, default: false },
  id: { type: String, default: undefined },
  tag: { type: String, default: 'div' },
})

const rendered = ref(props.open)
const visible = ref(props.open)

watch(
  () => props.open,
  (open) => {
    if (open) {
      rendered.value = true
      // Nästa frame: starta övergången från 0fr.
      requestAnimationFrame(() => requestAnimationFrame(() => (visible.value = true)))
    } else {
      visible.value = false
    }
  },
)

function onEnd(e) {
  if (e.target !== e.currentTarget || e.propertyName !== 'grid-template-rows') return
  if (!props.open) rendered.value = false
}
</script>

<template>
  <component :is="tag" :id="id" class="expander" :class="{ open: visible }" @transitionend="onEnd">
    <div class="inner">
      <slot v-if="rendered" />
    </div>
  </component>
</template>

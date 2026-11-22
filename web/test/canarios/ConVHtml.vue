<script setup lang="ts">
// Canario de la Spec 011 T-23: un sumidero HTML que el lint de producción debe rechazar. Excluido del lint normal.
const html = '<b>texto</b>'
</script>

<template>
  <div v-html="html"></div>
</template>

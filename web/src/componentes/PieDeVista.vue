<script setup lang="ts">
import { computed } from 'vue'
import { horaEnBogota } from '../formato/fechas'

const props = defineProps<{ cargadoEn: Date | undefined }>()
defineEmits<{ actualizar: [] }>()

const hora = computed(() => (props.cargadoEn ? horaEnBogota(props.cargadoEn) : null))
</script>

<template>
  <p class="pie">
    <span v-if="hora" data-cargado-en>Cargado a las {{ hora }} (hora de Bogotá)</span>
    <button type="button" data-accion="actualizar" @click="$emit('actualizar')">Actualizar</button>
  </p>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { rutaCatalogo } from '../api/rutas'
import type { Catalogo } from '../api/tipos'
import { usarRecurso } from './usarRecurso'

// Solo lee el almacén: las pestañas no piden nada. Sin el catálogo muestran el código del torneo (R-37).
const props = defineProps<{ slug: string; codigo: string; orden: number | undefined }>()
const { datos } = usarRecurso<Catalogo>(() => rutaCatalogo(props.slug))

const torneo = computed(() => datos.value?.torneos.find((t) => t.codigo === props.codigo))
const ordenDeLaFase = computed(
  () => props.orden ?? torneo.value?.fases.reduce<number | undefined>((m, f) => (m === undefined || f.orden < m ? f.orden : m), undefined) ?? 1,
)
</script>

<template>
  <header class="torneo">
    <h1>{{ torneo?.nombre ?? codigo }}</h1>
    <nav aria-label="Secciones del torneo">
      <RouterLink :to="{ name: 'partidos', params: { slug, codigo, orden: String(ordenDeLaFase) } }">Partidos</RouterLink>
      <RouterLink :to="{ name: 'posiciones', params: { slug, codigo, orden: String(ordenDeLaFase) } }">Posiciones</RouterLink>
      <RouterLink :to="{ name: 'goleadores', params: { slug, codigo } }">Goleadores</RouterLink>
      <RouterLink :to="{ name: 'valla', params: { slug, codigo } }">Valla menos vencida</RouterLink>
    </nav>
  </header>
</template>

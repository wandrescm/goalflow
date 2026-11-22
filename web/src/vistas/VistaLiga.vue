<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import EstadoDeRecurso from '../componentes/EstadoDeRecurso.vue'
import PieDeVista from '../componentes/PieDeVista.vue'
import { usarRecurso } from '../componentes/usarRecurso'
import { rutaCatalogo } from '../api/rutas'
import type { Catalogo } from '../api/tipos'

const ruta = useRoute()
const slug = computed(() => String(ruta.params.slug))
const rutaApi = computed(() => rutaCatalogo(slug.value))
const { estado, datos, cargadoEn, actualizar } = usarRecurso<Catalogo>(() => rutaApi.value)
</script>

<template>
  <section data-vista="liga">
    <EstadoDeRecurso v-if="!datos || estado.tipo !== 'ok'" :estado="estado" :ruta="rutaApi" />
    <template v-else>
      <h1>{{ datos.liga.nombre }}</h1>
      <p v-if="datos.torneos.length === 0">Esta liga todavía no tiene torneos.</p>
      <ul v-else>
        <li v-for="torneo in datos.torneos" :key="torneo.codigo">
          <RouterLink :to="{ name: 'torneo', params: { slug, codigo: torneo.codigo } }">{{ torneo.nombre }}</RouterLink>
        </li>
      </ul>
      <PieDeVista :cargado-en="cargadoEn" @actualizar="actualizar" />
    </template>
  </section>
</template>

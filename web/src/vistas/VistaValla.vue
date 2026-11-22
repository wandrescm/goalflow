<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import EstadoDeRecurso from '../componentes/EstadoDeRecurso.vue'
import PestanasDeTorneo from '../componentes/PestanasDeTorneo.vue'
import PieDeVista from '../componentes/PieDeVista.vue'
import { usarRecurso } from '../componentes/usarRecurso'
import { rutaValla } from '../api/rutas'
import type { Valla } from '../api/tipos'

const ruta = useRoute()
const slug = computed(() => String(ruta.params.slug))
const codigo = computed(() => String(ruta.params.codigo))
const rutaApi = computed(() => rutaValla(slug.value, codigo.value))
const { estado, datos, cargadoEn, actualizar } = usarRecurso<Valla>(() => rutaApi.value)
</script>

<template>
  <section data-vista="valla">
    <PestanasDeTorneo :slug="slug" :codigo="codigo" :orden="undefined" />
    <EstadoDeRecurso v-if="!datos || estado.tipo !== 'ok'" :estado="estado" :ruta="rutaApi" />
    <template v-else>
      <h2>Valla menos vencida</h2>
      <p v-if="datos.reglamento?.estado === 'Provisional'" data-marca="provisional">Reglamento provisional</p>
      <div class="tabla">
        <table>
          <thead>
            <tr>
              <th>Pos.</th>
              <th>Equipo</th>
              <th>Partidos</th>
              <th>Goles en contra</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="fila in datos.filas" :key="fila.equipo.codigo">
              <td>{{ fila.posicion }}</td>
              <td>{{ fila.equipo.nombre }}</td>
              <td>{{ fila.partidosComputados }}</td>
              <td>{{ fila.golesEnContra }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <PieDeVista :cargado-en="cargadoEn" @actualizar="actualizar" />
    </template>
  </section>
</template>

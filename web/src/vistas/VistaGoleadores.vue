<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import EstadoDeRecurso from '../componentes/EstadoDeRecurso.vue'
import PestanasDeTorneo from '../componentes/PestanasDeTorneo.vue'
import PieDeVista from '../componentes/PieDeVista.vue'
import { usarRecurso } from '../componentes/usarRecurso'
import { rutaGoleadores } from '../api/rutas'
import type { Goleadores } from '../api/tipos'

const ruta = useRoute()
const slug = computed(() => String(ruta.params.slug))
const codigo = computed(() => String(ruta.params.codigo))
const rutaApi = computed(() => rutaGoleadores(slug.value, codigo.value))
const { estado, datos, cargadoEn, actualizar } = usarRecurso<Goleadores>(() => rutaApi.value)
</script>

<template>
  <section data-vista="goleadores">
    <PestanasDeTorneo :slug="slug" :codigo="codigo" :orden="undefined" />
    <EstadoDeRecurso v-if="!datos || estado.tipo !== 'ok'" :estado="estado" :ruta="rutaApi" />
    <template v-else>
      <h2>Goleadores</h2>
      <p v-if="datos.reglamento?.estado === 'Provisional'" data-marca="provisional">Reglamento provisional</p>
      <div class="tabla">
        <table>
          <thead>
            <tr>
              <th>Pos.</th>
              <th>Jugador</th>
              <th>Equipo</th>
              <th>Goles</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="fila in datos.filas" :key="fila.jugador.codigo">
              <td>{{ fila.posicion }}</td>
              <td>{{ fila.jugador.nombre }}</td>
              <td>{{ fila.equipo.nombre }}</td>
              <td>{{ fila.goles }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p>Partidos computables: {{ datos.cobertura.partidosComputables }}</p>
      <p>Goles totales: {{ datos.cobertura.golesTotales }}</p>
      <p>Goles sin autor registrado: {{ datos.cobertura.golesSinAutor }}</p>
      <PieDeVista :cargado-en="cargadoEn" @actualizar="actualizar" />
    </template>
  </section>
</template>

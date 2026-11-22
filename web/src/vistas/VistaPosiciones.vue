<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import EstadoDeRecurso from '../componentes/EstadoDeRecurso.vue'
import PestanasDeTorneo from '../componentes/PestanasDeTorneo.vue'
import PieDeVista from '../componentes/PieDeVista.vue'
import { usarRecurso } from '../componentes/usarRecurso'
import { rutaPosiciones } from '../api/rutas'
import type { Posiciones } from '../api/tipos'

const ruta = useRoute()
const slug = computed(() => String(ruta.params.slug))
const codigo = computed(() => String(ruta.params.codigo))
const orden = computed(() => Number(ruta.params.orden))
const rutaApi = computed(() => rutaPosiciones(slug.value, codigo.value, orden.value))
const { estado, datos, cargadoEn, actualizar } = usarRecurso<Posiciones>(() => rutaApi.value)

// Los empates traen códigos de equipo; el nombre se toma de las filas, sin reordenar ni recalcular nada (R-43).
const nombreDe = (codigoDeEquipo: string) => datos.value?.filas.find((f) => f.equipo.codigo === codigoDeEquipo)?.equipo.nombre ?? codigoDeEquipo
</script>

<template>
  <section data-vista="posiciones">
    <PestanasDeTorneo :slug="slug" :codigo="codigo" :orden="orden" />
    <EstadoDeRecurso v-if="!datos || estado.tipo !== 'ok'" :estado="estado" :ruta="rutaApi" />
    <template v-else>
      <h2>Posiciones de la fase {{ datos.fase.orden }}</h2>
      <p v-if="datos.reglamento?.estado === 'Provisional'" data-marca="provisional">Reglamento provisional</p>
      <div class="tabla">
        <table>
          <thead>
            <tr>
              <th>Pos.</th>
              <th>Equipo</th>
              <th>PJ</th>
              <th>PG</th>
              <th>PE</th>
              <th>PP</th>
              <th>GF</th>
              <th>GC</th>
              <th>DG</th>
              <th>Puntos por resultado</th>
              <th>Deducciones</th>
              <th>Puntos</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="fila in datos.filas" :key="fila.equipo.codigo">
              <td>{{ fila.posicion }}</td>
              <td>{{ fila.equipo.nombre }}</td>
              <td>{{ fila.pj }}</td>
              <td>{{ fila.pg }}</td>
              <td>{{ fila.pe }}</td>
              <td>{{ fila.pp }}</td>
              <td>{{ fila.gf }}</td>
              <td>{{ fila.gc }}</td>
              <td>{{ fila.dg }}</td>
              <td>{{ fila.puntosPorResultado }}</td>
              <td>{{ fila.deducciones }}</td>
              <td>{{ fila.puntos }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <ul v-if="datos.empates.length > 0">
        <li v-for="empate in datos.empates" :key="empate.posicion">
          Empate sin resolver en la posición {{ empate.posicion }}: {{ empate.equipos.map(nombreDe).join(', ') }}
        </li>
      </ul>
      <p>Partidos con tarjetas computables: {{ datos.cobertura.partidosConTarjetasComputables }}</p>
      <p>Partidos con tarjetas sin informar: {{ datos.cobertura.partidosConTarjetasSinInformar }}</p>
      <PieDeVista :cargado-en="cargadoEn" @actualizar="actualizar" />
    </template>
  </section>
</template>

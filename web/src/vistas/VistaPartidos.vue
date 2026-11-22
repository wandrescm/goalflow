<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import EstadoDeRecurso from '../componentes/EstadoDeRecurso.vue'
import PestanasDeTorneo from '../componentes/PestanasDeTorneo.vue'
import PieDeVista from '../componentes/PieDeVista.vue'
import { usarRecurso } from '../componentes/usarRecurso'
import { rutaPartidos } from '../api/rutas'
import type { PartidosDeLaFase } from '../api/tipos'
import { fechaHoraEnBogota } from '../formato/fechas'
import { textoDeResultado } from '../formato/resultado'

const ruta = useRoute()
const slug = computed(() => String(ruta.params.slug))
const codigo = computed(() => String(ruta.params.codigo))
const orden = computed(() => Number(ruta.params.orden))
const rutaApi = computed(() => rutaPartidos(slug.value, codigo.value, orden.value))
const { estado, datos, cargadoEn, actualizar } = usarRecurso<PartidosDeLaFase>(() => rutaApi.value)
</script>

<template>
  <section data-vista="partidos">
    <PestanasDeTorneo :slug="slug" :codigo="codigo" :orden="orden" />
    <EstadoDeRecurso v-if="!datos || estado.tipo !== 'ok'" :estado="estado" :ruta="rutaApi" />
    <template v-else>
      <h2>Partidos de la fase {{ datos.fase.orden }}</h2>
      <div class="tabla">
        <table>
          <thead>
            <tr>
              <th>Jornada</th>
              <th>Fecha</th>
              <th>Partido</th>
              <th>Resultado</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="partido in datos.partidos" :key="`${partido.local.codigo}-${partido.visitante.codigo}`">
              <td>{{ partido.jornada }}</td>
              <td>{{ partido.programadoPara === null ? '—' : fechaHoraEnBogota(partido.programadoPara) }}</td>
              <td>
                <!-- R-36: el enlace al detalle se construye solo con los códigos del cuerpo de la lista. -->
                <RouterLink
                  :to="{ name: 'detalle', params: { slug, codigo, orden: String(orden), local: partido.local.codigo, visitante: partido.visitante.codigo } }"
                >
                  {{ partido.local.nombre }} – {{ partido.visitante.nombre }}
                </RouterLink>
              </td>
              <td>
                <span>{{ textoDeResultado(partido).principal }}</span>
                <small v-if="textoDeResultado(partido).etiqueta"> {{ textoDeResultado(partido).etiqueta }}</small>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <PieDeVista :cargado-en="cargadoEn" @actualizar="actualizar" />
    </template>
  </section>
</template>

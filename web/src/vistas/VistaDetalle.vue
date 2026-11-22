<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import EstadoDeRecurso from '../componentes/EstadoDeRecurso.vue'
import PieDeVista from '../componentes/PieDeVista.vue'
import { usarRecurso } from '../componentes/usarRecurso'
import { rutaDetalle } from '../api/rutas'
import type { DetalleDePartido } from '../api/tipos'
import { fechaHoraEnBogota } from '../formato/fechas'
import { textoDeResultado } from '../formato/resultado'

const ruta = useRoute()
const slug = computed(() => String(ruta.params.slug))
const codigo = computed(() => String(ruta.params.codigo))
const orden = computed(() => Number(ruta.params.orden))
const rutaApi = computed(() =>
  rutaDetalle(slug.value, codigo.value, orden.value, String(ruta.params.local), String(ruta.params.visitante)),
)
const { estado, datos, cargadoEn, actualizar } = usarRecurso<DetalleDePartido>(() => rutaApi.value)

const resultado = computed(() => (datos.value ? textoDeResultado(datos.value) : null))
const nombreDe = (lado: 'local' | 'visitante') => (lado === 'local' ? datos.value?.local.nombre : datos.value?.visitante.nombre)
const origenDelResumen = computed(() =>
  datos.value?.resumen?.origen === 'Modelo' ? 'Generado por el modelo' : 'Generado con una plantilla',
)
</script>

<template>
  <section data-vista="detalle">
    <EstadoDeRecurso v-if="!datos || estado.tipo !== 'ok'" :estado="estado" :ruta="rutaApi" />
    <template v-else>
      <p>
        <RouterLink :to="{ name: 'partidos', params: { slug, codigo, orden: String(orden) } }">← Partidos de {{ datos.torneo.nombre }}</RouterLink>
      </p>
      <h1>{{ datos.local.nombre }} – {{ datos.visitante.nombre }}</h1>
      <p>Jornada {{ datos.jornada }}<template v-if="datos.programadoPara !== null"> · {{ fechaHoraEnBogota(datos.programadoPara) }}</template></p>
      <p v-if="resultado" class="resultado">
        <strong>{{ resultado.principal }}</strong>
        <span v-if="resultado.etiqueta"> {{ resultado.etiqueta }}</span>
      </p>

      <template v-if="datos.marcador !== null">
        <h2>Goles</h2>
        <ul v-if="datos.goles.length > 0">
          <li v-for="gol in datos.goles" :key="`${gol.equipo}-${gol.jugador.codigo}`">
            {{ gol.jugador.nombre }} ({{ nombreDe(gol.equipo) }}) × {{ gol.cantidad }}
          </li>
        </ul>
        <template v-if="datos.autogoles.length > 0">
          <h2>Autogoles</h2>
          <ul>
            <li v-for="autogol in datos.autogoles" :key="`${autogol.equipoDelAutor}-${autogol.jugador.codigo}`">
              {{ autogol.jugador.nombre }} ({{ nombreDe(autogol.equipoDelAutor) }}), a favor de {{ nombreDe(autogol.equipoBeneficiado) }} × {{ autogol.cantidad }}
            </li>
          </ul>
        </template>
        <p v-if="datos.golesSinAutor && datos.golesSinAutor.local + datos.golesSinAutor.visitante > 0">
          Goles sin autor registrado: {{ datos.local.nombre }} {{ datos.golesSinAutor.local }}, {{ datos.visitante.nombre }} {{ datos.golesSinAutor.visitante }}
        </p>
      </template>

      <!-- R-44: tarjetas solo si se informaron; «no informadas» si es false; nada si es null. -->
      <template v-if="datos.tarjetasInformadas === true">
        <h2>Tarjetas</h2>
        <p v-if="datos.tarjetas.length === 0">Sin tarjetas</p>
        <ul v-else>
          <li v-for="tarjeta in datos.tarjetas" :key="`${tarjeta.equipo}-${tarjeta.jugador.codigo}-${tarjeta.color}`">
            {{ tarjeta.jugador.nombre }} ({{ nombreDe(tarjeta.equipo) }}): {{ tarjeta.color }} × {{ tarjeta.cantidad }}
          </li>
        </ul>
      </template>
      <p v-else-if="datos.tarjetasInformadas === false">Tarjetas no informadas</p>

      <!-- R-44, Spec 008 C-07: sin resumen publicado no hay sección ni mensaje. -->
      <section v-if="datos.resumen !== null" data-resumen>
        <h2>Resumen</h2>
        <p>{{ datos.resumen.texto }}</p>
        <p>
          <small>{{ origenDelResumen }}</small>
        </p>
      </section>

      <PieDeVista :cargado-en="cargadoEn" @actualizar="actualizar" />
    </template>
  </section>
</template>

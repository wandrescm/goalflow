<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useAlmacen, type EstadoDeCarga } from '../almacen/almacen'

const props = defineProps<{ estado: EstadoDeCarga; ruta: string }>()
const almacen = useAlmacen()

// R-39: con un Retry-After entero, el botón queda deshabilitado con cuenta regresiva hasta 0; sin él, habilitado de inmediato.
const restante = ref<number | null>(null)
let intervalo: ReturnType<typeof setInterval> | undefined

function detener() {
  if (intervalo !== undefined) clearInterval(intervalo)
  intervalo = undefined
}

watch(
  () => props.estado,
  (estado) => {
    detener()
    if (estado.tipo === 'ocupado' && estado.reintentarEn !== null && estado.reintentarEn > 0) {
      restante.value = estado.reintentarEn
      intervalo = setInterval(() => {
        restante.value = Math.max(0, (restante.value ?? 0) - 1)
        if (restante.value === 0) detener()
      }, 1000)
    } else {
      restante.value = null
    }
  },
  { immediate: true },
)
onBeforeUnmount(detener)

const esperando = computed(() => restante.value !== null && restante.value > 0)
const reintentable = computed(() => ['ocupado', 'tiempo-agotado', 'error'].includes(props.estado.tipo))
const faltantes = computed(() => (props.estado.tipo === 'configuracion-pendiente' ? props.estado.faltantes : []))

// Nunca reintenta por sí mismo: solo un clic del usuario pide de nuevo (R-39, R-40, R-41).
function reintentar() {
  void almacen.actualizar(props.ruta)
}
</script>

<template>
  <div class="estado" :data-estado="estado.tipo">
    <template v-if="estado.tipo === 'cargando' || estado.tipo === 'sin-cargar'">
      <p role="status">Cargando…</p>
      <p v-if="estado.tipo === 'cargando' && estado.lento">El servidor puede estar arrancando. Esto puede tardar unos segundos.</p>
    </template>
    <p v-else-if="estado.tipo === 'no-encontrado'">No encontrado. Revisa el enlace.</p>
    <template v-else-if="estado.tipo === 'configuracion-pendiente'">
      <p>Esta tabla espera la configuración de la liga.</p>
      <p v-if="faltantes.length > 0">Falta configurar: {{ faltantes.join(', ') }}.</p>
    </template>
    <p v-else-if="estado.tipo === 'ocupado'">El servicio está ocupado. Intenta de nuevo en unos segundos.</p>
    <p v-else-if="estado.tipo === 'tiempo-agotado'">La solicitud tardó demasiado. Intenta de nuevo.</p>
    <p v-else-if="estado.tipo === 'error'">No pudimos cargar la información. Intenta de nuevo.</p>

    <p v-if="reintentable">
      <button type="button" data-accion="reintentar" :disabled="esperando" @click="reintentar">Reintentar</button>
      <span v-if="esperando" data-cuenta-regresiva> Disponible en {{ restante }} s</span>
    </p>
  </div>
</template>

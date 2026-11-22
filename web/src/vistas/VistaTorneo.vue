<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAlmacen } from '../almacen/almacen'
import EstadoDeRecurso from '../componentes/EstadoDeRecurso.vue'
import { usarRecurso } from '../componentes/usarRecurso'
import { rutaCatalogo } from '../api/rutas'
import type { Catalogo } from '../api/tipos'
import { destinoDelTorneo } from '../enrutador/destinos'

// Solo se llega aquí si el catálogo falló al entrar al torneo (el enrutador no redirige ni reintenta). Tras un «Reintentar» manual
// que lo carga, esta vista completa la redirección de R-34.
const ruta = useRoute()
const enrutador = useRouter()
const almacen = useAlmacen()
const slug = computed(() => String(ruta.params.slug))
const codigo = computed(() => String(ruta.params.codigo))
const rutaApi = computed(() => rutaCatalogo(slug.value))
const { estado } = usarRecurso<Catalogo>(() => rutaApi.value)

watch(
  () => almacen.datosDe<Catalogo>(rutaApi.value),
  (catalogo) => {
    if (!catalogo) return
    void enrutador.replace(
      destinoDelTorneo(catalogo, slug.value, codigo.value) ?? { name: 'no-encontrado', params: { pathMatch: ruta.path.split('/').slice(1) } },
    )
  },
)
</script>

<template>
  <section data-vista="torneo">
    <h1>{{ codigo }}</h1>
    <EstadoDeRecurso :estado="estado" :ruta="rutaApi" />
  </section>
</template>

import type { Pinia } from 'pinia'
import { createRouter, type RouteLocationNormalized, type RouteLocationRaw, type Router, type RouterHistory } from 'vue-router'
import { useAlmacen } from '../almacen/almacen'
import { rutaCatalogo, rutaDetalle, rutaGoleadores, rutaPartidos, rutaPosiciones, rutaValla } from '../api/rutas'
import type { Catalogo } from '../api/tipos'
import { destinoDelTorneo } from './destinos'
import VistaDetalle from '../vistas/VistaDetalle.vue'
import VistaGoleadores from '../vistas/VistaGoleadores.vue'
import VistaInicio from '../vistas/VistaInicio.vue'
import VistaLiga from '../vistas/VistaLiga.vue'
import VistaNoEncontrado from '../vistas/VistaNoEncontrado.vue'
import VistaPartidos from '../vistas/VistaPartidos.vue'
import VistaPosiciones from '../vistas/VistaPosiciones.vue'
import VistaTorneo from '../vistas/VistaTorneo.vue'
import VistaValla from '../vistas/VistaValla.vue'

const texto = (valor: string | string[] | undefined): string => (Array.isArray(valor) ? valor.join('/') : (valor ?? ''))

/**
 * Los recursos de la API que necesita una ruta (Spec 011 R-37): la liga, 1 (los torneos); una pestaña de torneo, los torneos y el
 * recurso de la pestaña; un detalle, solo el detalle. El resto no pide nada.
 */
export function recursosDe(ruta: RouteLocationNormalized): string[] {
  const slug = texto(ruta.params.slug)
  const codigo = texto(ruta.params.codigo)
  const orden = Number(texto(ruta.params.orden))
  switch (ruta.name) {
    case 'liga':
      return [rutaCatalogo(slug)]
    case 'partidos':
      return [rutaCatalogo(slug), rutaPartidos(slug, codigo, orden)]
    case 'posiciones':
      return [rutaCatalogo(slug), rutaPosiciones(slug, codigo, orden)]
    case 'goleadores':
      return [rutaCatalogo(slug), rutaGoleadores(slug, codigo)]
    case 'valla':
      return [rutaCatalogo(slug), rutaValla(slug, codigo)]
    case 'detalle':
      return [rutaDetalle(slug, codigo, orden, texto(ruta.params.local), texto(ruta.params.visitante))]
    default:
      return []
  }
}

/** El enrutador de la aplicación: las rutas de R-34, la redirección del torneo y la carga de los recursos de cada navegación (R-37). */
export function crearEnrutador(historial: RouterHistory, pinia: Pinia): Router {
  const almacen = useAlmacen(pinia)

  const noEncontrado = (hacia: RouteLocationNormalized): RouteLocationRaw => ({
    name: 'no-encontrado',
    params: { pathMatch: hacia.path.split('/').slice(1) },
    query: hacia.query,
    hash: hacia.hash,
  })

  const enrutador = createRouter({
    history: historial,
    routes: [
      { path: '/', name: 'inicio', component: VistaInicio },
      { path: '/:slug', name: 'liga', component: VistaLiga },
      {
        // R-34: redirige a los partidos de la fase de menor orden del catálogo, que se carga si no está en el almacén.
        path: '/:slug/torneos/:codigo',
        name: 'torneo',
        component: VistaTorneo,
        beforeEnter: async (hacia) => {
          const ruta = rutaCatalogo(texto(hacia.params.slug))
          await almacen.asegurar(ruta)
          if (almacen.estadoDe(ruta).tipo === 'no-encontrado') return noEncontrado(hacia)

          const catalogo = almacen.datosDe<Catalogo>(ruta)
          // Un error del catálogo no redirige ni reintenta: la vista del torneo muestra su estado y un botón manual (R-40).
          if (!catalogo) return true

          return destinoDelTorneo(catalogo, texto(hacia.params.slug), texto(hacia.params.codigo)) ?? noEncontrado(hacia)
        },
      },
      { path: '/:slug/torneos/:codigo/fases/:orden([1-9]\\d*)/partidos', name: 'partidos', component: VistaPartidos },
      { path: '/:slug/torneos/:codigo/fases/:orden([1-9]\\d*)/posiciones', name: 'posiciones', component: VistaPosiciones },
      { path: '/:slug/torneos/:codigo/goleadores', name: 'goleadores', component: VistaGoleadores },
      { path: '/:slug/torneos/:codigo/valla', name: 'valla', component: VistaValla },
      {
        path: '/:slug/torneos/:codigo/fases/:orden([1-9]\\d*)/partidos/:local/:visitante',
        name: 'detalle',
        component: VistaDetalle,
      },
      { path: '/:pathMatch(.*)*', name: 'no-encontrado', component: VistaNoEncontrado },
    ],
  })

  // Sin precarga y sin sondeo: cada navegación pide, a lo sumo, lo que su ruta necesita y el almacén no tiene.
  enrutador.afterEach((hacia, _desde, fallo) => {
    if (fallo) return
    for (const ruta of recursosDe(hacia)) void almacen.asegurar(ruta)
  })

  return enrutador
}

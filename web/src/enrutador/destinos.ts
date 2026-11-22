import type { RouteLocationRaw } from 'vue-router'
import type { Catalogo } from '../api/tipos'

/** Los partidos de la fase de menor orden del torneo (R-34), o undefined si el catálogo no tiene el torneo o no tiene fases. */
export function destinoDelTorneo(catalogo: Catalogo, slug: string, codigo: string): RouteLocationRaw | undefined {
  const torneo = catalogo.torneos.find((t) => t.codigo === codigo)
  const menor = torneo?.fases.reduce<number | undefined>((m, f) => (m === undefined || f.orden < m ? f.orden : m), undefined)
  return menor === undefined ? undefined : { name: 'partidos', params: { slug, codigo, orden: String(menor) }, replace: true }
}

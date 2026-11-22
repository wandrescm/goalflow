/** Los seis constructores de ruta de la API (Spec 011 R-36). Cada valor de la ruta se codifica con encodeURIComponent. */

const e = encodeURIComponent

export function rutaCatalogo(slug: string): string {
  return `/${e(slug)}/torneos`
}

export function rutaPartidos(slug: string, codigo: string, orden: number): string {
  return `/${e(slug)}/torneos/${e(codigo)}/fases/${e(String(orden))}/partidos`
}

export function rutaDetalle(slug: string, codigo: string, orden: number, local: string, visitante: string): string {
  return `${rutaPartidos(slug, codigo, orden)}/${e(local)}/${e(visitante)}`
}

export function rutaPosiciones(slug: string, codigo: string, orden: number): string {
  return `/${e(slug)}/torneos/${e(codigo)}/fases/${e(String(orden))}/posiciones`
}

export function rutaGoleadores(slug: string, codigo: string): string {
  return `/${e(slug)}/torneos/${e(codigo)}/goleadores`
}

export function rutaValla(slug: string, codigo: string): string {
  return `/${e(slug)}/torneos/${e(codigo)}/valla`
}

import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { instalarFetch, respuesta, URL_BASE } from './dobles'

export const SLUG = 'liga-humo'
export const T1 = 'HUMO-T1'
export const PARTIDOS = `/${SLUG}/torneos/${T1}/fases/1/partidos`
export const CATALOGO = `/${SLUG}/torneos`

/** Un fixture congelado del humo (A4), leído de web/test/fixtures. */
export function fixture<T = unknown>(nombre: string): T {
  // Con una ruta de texto: bajo jsdom, URL es la de jsdom y fs no la reconoce.
  return JSON.parse(readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', 'fixtures', `${nombre}.json`), 'utf8')) as T
}

/** Una respuesta de la API falsa: recibe las opciones de la petición (por ejemplo, su señal) y puede ser asíncrona. */
export type Manejador = (opciones: RequestInit) => Response | Promise<Response>

/** La tabla de rutas de la API falsa: los fixtures reales de producción. Cada entrada crea una Response nueva. */
export function tablaDeLaApiFalsa(): Record<string, Manejador> {
  return {
    [CATALOGO]: () => respuesta(200, fixture('S011-01')),
    [PARTIDOS]: () => respuesta(200, fixture('S011-02')),
    [`/${SLUG}/torneos/${T1}/fases/1/posiciones`]: () => respuesta(200, fixture('S009-01')),
    [`/${SLUG}/torneos/${T1}/goleadores`]: () => respuesta(200, fixture('S009-02')),
    [`/${SLUG}/torneos/${T1}/valla`]: () => respuesta(200, fixture('S009-03')),
    [`${PARTIDOS}/HUMO-T1-E1/HUMO-T1-E2`]: () => respuesta(200, fixture('S011-03-E1E2')),
    [`${PARTIDOS}/HUMO-T1-E1/HUMO-T1-E6`]: () => respuesta(200, fixture('S011-03-E1E6')),
    [`${PARTIDOS}/HUMO-T1-E3/HUMO-T1-E4`]: () => respuesta(200, fixture('S011-03-E3E4')),
    [`${PARTIDOS}/HUMO-T1-E3/HUMO-T1-E6`]: () => respuesta(200, fixture('S011-03-E3E6')),
    [`/${SLUG}/torneos/HUMO-T2/fases/1/posiciones`]: () => respuesta(409, fixture('S009-04')),
  }
}

/** Instala un fetch que responde con la API falsa y cuenta las llamadas; lo que no está en la tabla da 404. */
export function instalarApiFalsa(cambios: Record<string, Manejador> = {}) {
  const tabla: Record<string, Manejador> = { ...tablaDeLaApiFalsa(), ...cambios }
  const instalado = instalarFetch((url, opciones) => {
    const ruta = url.slice(URL_BASE.length)
    return tabla[ruta]?.(opciones) ?? respuesta(404)
  })
  return { ...instalado, rutasLlamadas: () => instalado.llamadas.map((l) => l.url.slice(URL_BASE.length)) }
}

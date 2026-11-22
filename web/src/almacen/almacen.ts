import { defineStore } from 'pinia'
import { shallowReactive } from 'vue'
import { pedir, type Resultado } from '../api/cliente'

type Fallo = Exclude<Resultado<unknown>, { tipo: 'ok' }>

/** Lo que una vista ve de un recurso (Spec 011 R-38, R-39 a R-41). */
export type EstadoDeCarga = { tipo: 'sin-cargar' } | { tipo: 'cargando'; lento: boolean } | { tipo: 'ok' } | Fallo

interface Entrada {
  datos: unknown
  cargadoEn: Date
}

/**
 * Cada respuesta buena se guarda por su ruta de la API mientras vive la página (R-38). Nada se guarda si la respuesta es un error, un
 * 404, un 409, un 429, un aborto o un tiempo agotado: la vista recibe su estado y decide. «Actualizar» reemplaza solo la entrada de
 * su ruta. Nada es persistente: recargar la página vacía el almacén.
 */
export const useAlmacen = defineStore('almacen', () => {
  const entradas = shallowReactive(new Map<string, Entrada>())
  const estados = shallowReactive(new Map<string, { tipo: 'cargando'; lento: boolean } | Fallo>())
  const enVuelo = new Map<string, Promise<void>>()

  async function cargar(ruta: string): Promise<void> {
    estados.set(ruta, { tipo: 'cargando', lento: false })
    const resultado = await pedir<unknown>(ruta, {
      alTardar: () => {
        if (estados.get(ruta)?.tipo === 'cargando') estados.set(ruta, { tipo: 'cargando', lento: true })
      },
    })

    if (resultado.tipo === 'ok') {
      entradas.set(ruta, { datos: resultado.datos, cargadoEn: new Date() })
      estados.delete(ruta)
    } else {
      estados.set(ruta, resultado)
    }
  }

  /** Una sola petición por ruta a la vez: quien pide lo que ya viaja recibe la misma promesa. */
  function iniciar(ruta: string): Promise<void> {
    const promesa = cargar(ruta).finally(() => enVuelo.delete(ruta))
    enVuelo.set(ruta, promesa)
    return promesa
  }

  /** Pide el recurso solo si el almacén no lo tiene (R-37). */
  function asegurar(ruta: string): Promise<void> {
    if (entradas.has(ruta)) return Promise.resolve()
    return enVuelo.get(ruta) ?? iniciar(ruta)
  }

  /** «Actualizar» (R-37 d, R-38): una petición, aunque ya esté en el almacén. */
  function actualizar(ruta: string): Promise<void> {
    return enVuelo.get(ruta) ?? iniciar(ruta)
  }

  async function esperarPendientes(): Promise<void> {
    while (enVuelo.size > 0) await Promise.all([...enVuelo.values()])
  }

  function datosDe<T>(ruta: string): T | undefined {
    return entradas.get(ruta)?.datos as T | undefined
  }

  function cargadoEnDe(ruta: string): Date | undefined {
    return entradas.get(ruta)?.cargadoEn
  }

  function estadoDe(ruta: string): EstadoDeCarga {
    const estado = estados.get(ruta)
    if (estado) return estado
    return entradas.has(ruta) ? { tipo: 'ok' } : { tipo: 'sin-cargar' }
  }

  return { asegurar, actualizar, esperarPendientes, datosDe, cargadoEnDe, estadoDe }
})

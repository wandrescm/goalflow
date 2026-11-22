import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { vi } from 'vitest'
import { createMemoryHistory } from 'vue-router'
import { useAlmacen } from '../../src/almacen/almacen'
import { crearEnrutador } from '../../src/enrutador/enrutador'
import AplicacionGoalFlow from '../../src/AplicacionGoalFlow.vue'

export interface Aplicacion {
  envoltorio: VueWrapper
  almacen: ReturnType<typeof useAlmacen>
  enrutador: ReturnType<typeof crearEnrutador>
}

/** La aplicación real con un almacén y un enrutador nuevos, en memoria, abierta en `ruta`. Con temporizadores falsos usa `asentar`. */
export async function abrir(ruta: string, { temporizadoresFalsos = false } = {}): Promise<Aplicacion> {
  const pinia = createPinia()
  setActivePinia(pinia)
  const enrutador = crearEnrutador(createMemoryHistory(), pinia)
  const envoltorio = mount(AplicacionGoalFlow, { global: { plugins: [pinia, enrutador] } })
  await enrutador.push(ruta)
  await enrutador.isReady()
  const almacen = useAlmacen(pinia)
  await (temporizadoresFalsos ? asentar() : liberar(almacen))
  return { envoltorio, almacen, enrutador }
}

async function liberar(almacen: ReturnType<typeof useAlmacen>) {
  await almacen.esperarPendientes()
  await flushPromises()
}

/** Deja que corran las promesas pendientes y el render, sin avanzar el reloj falso (flushPromises usaría un temporizador). */
export async function asentar(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await vi.advanceTimersByTimeAsync(0)
    await nextTick()
  }
}

/** Avanza el reloj falso y deja asentar el render. */
export async function avanzar(milisegundos: number): Promise<void> {
  await vi.advanceTimersByTimeAsync(milisegundos)
  await asentar()
}

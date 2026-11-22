import { computed } from 'vue'
import { useAlmacen } from '../almacen/almacen'

/** Lo que una vista necesita de un recurso del almacén: su estado, sus datos, la hora de carga y «Actualizar». No pide nada por sí misma. */
export function usarRecurso<T>(ruta: () => string) {
  const almacen = useAlmacen()
  return {
    estado: computed(() => almacen.estadoDe(ruta())),
    datos: computed(() => almacen.datosDe<T>(ruta())),
    cargadoEn: computed(() => almacen.cargadoEnDe(ruta())),
    actualizar: () => almacen.actualizar(ruta()),
  }
}

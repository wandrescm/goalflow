/** Las partes de una fecha en America/Bogota, con día y mes de dos dígitos y la hora de 00 a 23 (Spec 011 R-45). */
export interface PartesDeFecha {
  dia: string
  mes: string
  anio: string
  hora: string
  minuto: string
}

// Con un timeZone explícito: nunca depende de la zona del dispositivo. hourCycle h23 evita «24» a medianoche.
const formateador = new Intl.DateTimeFormat('es-CO', {
  timeZone: 'America/Bogota',
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

export function partesEnBogota(instante: string | Date): PartesDeFecha {
  const partes = formateador.formatToParts(typeof instante === 'string' ? new Date(instante) : instante)
  const valor = (tipo: Intl.DateTimeFormatPartTypes) => partes.find((p) => p.type === tipo)?.value ?? ''
  return { dia: valor('day'), mes: valor('month'), anio: valor('year'), hora: valor('hour'), minuto: valor('minute') }
}

/** «dd/MM/aaaa HH:mm» en Bogotá. */
export function fechaHoraEnBogota(instante: string | Date): string {
  const p = partesEnBogota(instante)
  return `${p.dia}/${p.mes}/${p.anio} ${p.hora}:${p.minuto}`
}

/** «HH:mm» en Bogotá: la hora de carga que muestra cada vista (R-38). */
export function horaEnBogota(instante: Date): string {
  const p = partesEnBogota(instante)
  return `${p.hora}:${p.minuto}`
}

/** Los cuerpos de la API que consume el frontend (Spec 011 §3.1 a §3.4; Spec 009 R-27). Sin lógica: solo la forma de los datos. */

export interface Equipo {
  codigo: string
  nombre: string
}

export interface Jugador {
  codigo: string
  nombre: string
}

export interface Marcador {
  local: number
  visitante: number
}

export type EstadoDePartido = 'Programado' | 'Cancelado' | 'Jugado' | 'Suspendido' | 'Adjudicado'
export type MotivoDeResultado = 'Clima' | 'Luz' | 'Disturbios' | 'WalkOver' | 'Sancion' | 'Otro'

// ---- R-01: catálogo de la liga ----

export interface FaseDelCatalogo {
  orden: number
  tipo: string
  vueltas: number
}

export interface TorneoDelCatalogo {
  codigo: string
  nombre: string
  fases: FaseDelCatalogo[]
}

export interface Catalogo {
  liga: { slug: string; nombre: string }
  torneos: TorneoDelCatalogo[]
}

// ---- R-04: partidos de una fase ----

export interface PartidoDeLaLista {
  jornada: number
  programadoPara: string | null
  local: Equipo
  visitante: Equipo
  estado: EstadoDePartido
  marcador: Marcador | null
  oficial: Marcador | null
  motivo: MotivoDeResultado | null
}

export interface PartidosDeLaFase {
  torneo: { codigo: string; nombre: string }
  fase: { orden: number }
  partidos: PartidoDeLaLista[]
}

// ---- R-09: detalle de un partido ----

export interface GolDelDetalle {
  equipo: 'local' | 'visitante'
  jugador: Jugador
  cantidad: number
}

export interface AutogolDelDetalle {
  equipoDelAutor: 'local' | 'visitante'
  equipoBeneficiado: 'local' | 'visitante'
  jugador: Jugador
  cantidad: number
}

export interface TarjetaDelDetalle {
  equipo: 'local' | 'visitante'
  jugador: Jugador
  color: 'Amarilla' | 'Azul' | 'Roja'
  cantidad: number
}

export interface ResumenPublicado {
  texto: string
  origen: 'Modelo' | 'Plantilla'
}

export interface DetalleDePartido {
  torneo: { codigo: string; nombre: string }
  fase: { orden: number }
  jornada: number
  programadoPara: string | null
  local: Equipo
  visitante: Equipo
  estado: EstadoDePartido
  marcador: Marcador | null
  oficial: Marcador | null
  motivo: MotivoDeResultado | null
  goles: GolDelDetalle[]
  autogoles: AutogolDelDetalle[]
  golesSinAutor: Marcador | null
  tarjetasInformadas: boolean | null
  tarjetas: TarjetaDelDetalle[]
  resumen: ResumenPublicado | null
}

// ---- Spec 009 R-27: posiciones, goleadores y valla (se muestran tal como llegan) ----

export interface Reglamento {
  version: number
  estado: 'Provisional' | 'Confirmado'
}

export interface FilaDePosicion {
  posicion: number
  equipo: Equipo
  pj: number
  pg: number
  pe: number
  pp: number
  gf: number
  gc: number
  dg: number
  puntosPorResultado: number
  deducciones: number
  puntos: number
  partidosConTarjetasComputables: number
  partidosConTarjetasSinInformar: number
}

export interface CriterioOmitido {
  criterio: string
  motivo: string
  equipos: { codigo: string; partidosSinInformar: number }[]
}

export interface EmpateSinResolver {
  posicion: number
  equipos: string[]
  motivo: string
  criteriosOmitidos: CriterioOmitido[]
}

export interface Posiciones {
  torneo: { codigo: string; nombre: string }
  reglamento: Reglamento | null
  fase: { orden: number }
  filas: FilaDePosicion[]
  empates: EmpateSinResolver[]
  cobertura: { partidosConTarjetasComputables: number; partidosConTarjetasSinInformar: number }
}

export interface FilaDeGoleador {
  posicion: number
  jugador: Jugador
  equipo: Equipo
  goles: number
}

export interface Goleadores {
  torneo: { codigo: string; nombre: string }
  reglamento: Reglamento | null
  filas: FilaDeGoleador[]
  cobertura: {
    partidosComputables: number
    partidosConGolesSinAutor: number
    golesTotales: number
    golesDeJugador: number
    autogoles: number
    golesSinAutor: number
    golesConAutor: number
    porEquipo: { equipo: Equipo; partidosComputables: number; partidosConGolesSinAutor: number; golesSinAutor: number }[]
  }
}

export interface FilaDeValla {
  posicion: number
  equipo: Equipo
  partidosComputados: number
  golesEnContra: number
}

export interface Valla {
  torneo: { codigo: string; nombre: string }
  reglamento: Reglamento | null
  filas: FilaDeValla[]
}

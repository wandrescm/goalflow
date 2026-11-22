/** La URL pública de la API (Spec 011 R-36, R-47): la única variable de entorno del bundle. Sin barra final. Se lee al usarla. */
export function urlBaseDeLaApi(): string {
  return import.meta.env.VITE_API_BASE_URL
}

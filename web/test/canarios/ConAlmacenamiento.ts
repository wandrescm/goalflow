// Canario de la Spec 011 R-47: almacenamiento del navegador y cookies. Excluido del lint normal.
export function guardar(): void {
  localStorage.setItem('clave', 'valor')
  document.cookie = 'a=1'
}

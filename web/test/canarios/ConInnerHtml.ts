// Canario de la Spec 011 T-23: un sumidero HTML por la API del DOM. Excluido del lint normal.
export function escribirHtml(elemento: HTMLElement, texto: string): void {
  elemento.innerHTML = texto
}

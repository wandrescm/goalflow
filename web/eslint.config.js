import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
import pluginVue from 'eslint-plugin-vue'

const SUMIDERO = 'Los textos de equipos, jugadores y resúmenes son datos no confiables: solo se muestran por interpolación (Spec 011 R-46).'
const ALMACENAMIENTO = 'El frontend no usa almacenamiento del navegador ni cookies (Spec 011 R-47).'

export default defineConfigWithVueTs(
  { ignores: ['dist/**', 'node_modules/**', '.wrangler/**', 'test/canarios/**'] },
  pluginVue.configs['flat/essential'],
  vueTsConfigs.recommended,
  {
    rules: {
      'vue/no-v-html': 'error',
      'no-restricted-globals': [
        'error',
        { name: 'localStorage', message: ALMACENAMIENTO },
        { name: 'sessionStorage', message: ALMACENAMIENTO },
      ],
      'no-restricted-properties': [
        'error',
        { object: 'document', property: 'cookie', message: ALMACENAMIENTO },
        { object: 'document', property: 'write', message: SUMIDERO },
        { object: 'window', property: 'localStorage', message: ALMACENAMIENTO },
        { object: 'window', property: 'sessionStorage', message: ALMACENAMIENTO },
        { object: 'globalThis', property: 'localStorage', message: ALMACENAMIENTO },
        { object: 'globalThis', property: 'sessionStorage', message: ALMACENAMIENTO },
      ],
      'no-restricted-syntax': [
        'error',
        { selector: "MemberExpression[property.name='innerHTML']", message: SUMIDERO },
        { selector: "MemberExpression[property.name='outerHTML']", message: SUMIDERO },
        { selector: "MemberExpression[computed=true][property.value='innerHTML']", message: SUMIDERO },
        { selector: "MemberExpression[computed=true][property.value='outerHTML']", message: SUMIDERO },
        { selector: "CallExpression[callee.property.name='insertAdjacentHTML']", message: SUMIDERO },
      ],
    },
  },
  {
    // T-25 (C-27): la prueba que inspecciona el almacenamiento del navegador para fijar que solo el enrutador lo toca. Solo este archivo
    // deja de aplicar las reglas de almacenamiento y cookies; las de HTML siguen activas.
    files: ['test/arquitectura/almacenamiento.test.ts'],
    rules: { 'no-restricted-globals': 'off', 'no-restricted-properties': 'off' },
  },
)

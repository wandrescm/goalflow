import { createPinia } from 'pinia'
import { createApp } from 'vue'
import { createWebHistory } from 'vue-router'
import AplicacionGoalFlow from './AplicacionGoalFlow.vue'
import { crearEnrutador } from './enrutador/enrutador'

const pinia = createPinia()
const enrutador = crearEnrutador(createWebHistory(), pinia)

createApp(AplicacionGoalFlow).use(pinia).use(enrutador).mount('#app')

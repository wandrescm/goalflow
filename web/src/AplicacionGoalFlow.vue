<template>
  <header class="barra">
    <RouterLink to="/">GoalFlow</RouterLink>
  </header>
  <main>
    <RouterView />
  </main>
</template>

<style>
body {
  margin: 0;
  font-family: system-ui, sans-serif;
  line-height: 1.5;
}

.barra,
main {
  max-width: 60rem;
  margin: 0 auto;
  padding: 0.5rem 1rem;
}

nav a {
  margin-right: 1rem;
}

.tabla {
  overflow-x: auto;
}

table {
  border-collapse: collapse;
  width: 100%;
}

th,
td {
  border-bottom: 1px solid #ccc;
  padding: 0.25rem 0.5rem;
  text-align: left;
}

[data-marca='provisional'] {
  display: inline-block;
  padding: 0.1rem 0.5rem;
  border: 1px solid #b58900;
  border-radius: 0.25rem;
}
</style>

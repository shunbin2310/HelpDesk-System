<script setup lang="ts">
import { useRoute } from 'vue-router'
const route = useRoute()
</script>
<template>
  <section class="card access-card">
    <p class="eyebrow">HELPDESK</p>
    <h1>
      {{
        route.query.reason === 'forbidden' ? 'Access restricted' : 'Unable to load your workspace'
      }}
    </h1>
    <p class="muted">
      {{
        route.query.reason === 'forbidden'
          ? 'Your role does not have access to this page.'
          : 'Check your connection and try again.'
      }}
    </p>
    <RouterLink
      to="/"
      class="button primary"
      >Return to workspace</RouterLink
    >
  </section>
</template>

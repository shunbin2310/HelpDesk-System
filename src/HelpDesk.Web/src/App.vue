<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getHealth } from './services/health'

type ConnectionState = 'checking' | 'connected' | 'unavailable'

const connection = ref<ConnectionState>('checking')
const lastCheckedAt = ref<string | null>(null)
const statusLabel = computed(() => ({
  checking: 'Checking connection',
  connected: 'Service connected',
  unavailable: 'Service unavailable',
})[connection.value])

async function checkConnection() {
  connection.value = 'checking'

  try {
    const health = await getHealth()
    connection.value = health.status === 'healthy' ? 'connected' : 'unavailable'
  } catch {
    connection.value = 'unavailable'
  } finally {
    lastCheckedAt.value = new Date().toLocaleTimeString()
  }
}

onMounted(checkConnection)
</script>

<template>
  <main class="workspace">
    <header class="brand">
      <span class="brand-mark" aria-hidden="true">H</span>
      <span>Helpdesk</span>
    </header>

    <section class="welcome-card" aria-labelledby="welcome-title">
      <p class="eyebrow">IT SUPPORT PORTAL</p>
      <h1 id="welcome-title">Your helpdesk starts here.</h1>
      <p class="intro">
        One place to report IT issues, follow updates, and get back to work.
      </p>

      <div class="connection-panel">
        <div class="connection-summary" role="status" aria-live="polite">
          <span class="status-dot" :class="connection" aria-hidden="true"></span>
          <div>
            <p class="status-label">{{ statusLabel }}</p>
            <p v-if="lastCheckedAt" class="last-checked">
              Last checked at {{ lastCheckedAt }}
            </p>
          </div>
        </div>
        <button
          type="button"
          :disabled="connection === 'checking'"
          @click="checkConnection"
        >
          {{ connection === 'checking' ? 'Checking…' : 'Check connection' }}
        </button>
      </div>

      <p v-if="connection === 'unavailable'" class="connection-help">
        We could not reach the support service. Please try again.
      </p>
    </section>

    <footer>Helpdesk System · Company IT Support</footer>
  </main>
</template>

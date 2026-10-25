<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { send } from '../services/api'
import { session, setSession, type Session } from '../services/session'

const router = useRouter()
const route = useRoute()
const email = ref('')
const password = ref('')
const busy = ref(false)
const error = ref('')
const errorBox = ref<HTMLElement>()

async function login() {
  busy.value = true
  error.value = ''
  try {
    const result = await send<Session>('/auth/login', 'POST', {
      email: email.value,
      password: password.value,
    })
    setSession(result)
    password.value = ''
    await router.replace('/')
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : 'Unable to sign in.'
    await nextTick()
    errorBox.value?.focus()
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login-layout">
    <section class="login-intro">
      <p class="eyebrow">COMPANY IT SUPPORT</p>
      <h1>A clear path <br />to better support.</h1>
      <p>One place to report IT issues and follow their progress.</p>
      <div class="intro-detail">
        <span
          class="brand-mark"
          aria-hidden="true"
          >H</span
        >
        <div>
          <strong>Helpdesk</strong>
          <p>Here to keep your work moving.</p>
        </div>
      </div>
    </section>
    <section
      class="card login-card"
      aria-labelledby="login-title"
    >
      <p class="eyebrow">WELCOME BACK</p>
      <h2 id="login-title">Sign in to Helpdesk</h2>
      <p class="muted">Use the account provided by your System Admin.</p>
      <p
        v-if="route.query.changed"
        class="notice"
        role="status"
      >
        Password updated. Sign in with your new password.
      </p>
      <p
        v-if="session"
        class="notice"
      >
        You already have a session. <RouterLink to="/">Continue to workspace</RouterLink>
      </p>
      <form @submit.prevent="login">
        <div
          v-if="error"
          ref="errorBox"
          class="error"
          role="alert"
          tabindex="-1"
        >
          {{ error }}
        </div>
        <label for="email">Email address</label>
        <input
          id="email"
          v-model.trim="email"
          name="email"
          type="email"
          autocomplete="username"
          required
          maxlength="256"
          placeholder="you@company.com"
        />
        <label for="password">Password</label>
        <input
          id="password"
          v-model="password"
          name="password"
          type="password"
          autocomplete="current-password"
          required
          maxlength="128"
        />
        <button
          class="primary full-width"
          :disabled="busy"
        >
          {{ busy ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>
      <p class="form-help">Need an account or a password reset?<br />Contact your System Admin.</p>
    </section>
  </div>
</template>

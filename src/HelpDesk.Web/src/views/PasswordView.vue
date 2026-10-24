<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { useRouter } from 'vue-router'
import { send } from '../services/api'
import { setSession } from '../services/session'
const router = useRouter()
const current = ref(''), password = ref(''), confirm = ref(''), error = ref(''), busy = ref(false)
const errorBox = ref<HTMLElement>()
async function save() {
  error.value = ''
  if (password.value !== confirm.value) { error.value = 'The new passwords do not match.'; await nextTick(); errorBox.value?.focus(); return }
  busy.value = true
  try {
    await send('/auth/change-password', 'POST', { currentPassword: current.value, newPassword: password.value })
    current.value = password.value = confirm.value = ''
    await router.replace({ path: '/login', query: { changed: '1' } })
    setSession(null)
  } catch (cause) { error.value = cause instanceof Error ? cause.message : 'Unable to change password.'; await nextTick(); errorBox.value?.focus() }
  finally { busy.value = false }
}
</script>
<template>
  <section class="page-heading"><p class="eyebrow">ACCOUNT SECURITY</p><h1>Change password</h1><p class="muted">You will sign in again after saving your new password.</p></section>
  <form class="card narrow" @submit.prevent="save">
    <div v-if="error" ref="errorBox" class="error" role="alert" tabindex="-1">{{ error }}</div>
    <label for="current">Current password</label><input id="current" v-model="current" type="password" autocomplete="current-password" required maxlength="128">
    <label for="new">New password</label><input id="new" v-model="password" type="password" autocomplete="new-password" required minlength="10" maxlength="128" aria-describedby="password-rules">
    <p id="password-rules" class="form-help">10–128 characters, with uppercase, lowercase, a number and a symbol.</p>
    <label for="confirm">Confirm new password</label><input id="confirm" v-model="confirm" type="password" autocomplete="new-password" required minlength="10" maxlength="128">
    <div class="actions"><button class="primary" :disabled="busy">{{ busy ? 'Saving…' : 'Save password' }}</button><RouterLink class="button" to="/">Cancel</RouterLink></div>
  </form>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { request, send } from '../services/api'
import { roles, session, setSession, type UserAccount, type Role } from '../services/session'

interface Page {
  items: UserAccount[]
  total: number
  page: number
}
type Mode = 'create' | 'role' | 'password' | 'active'
const data = ref<Page>({ items: [], total: 0, page: 1 })
const page = ref(1),
  loading = ref(false),
  busy = ref(false),
  error = ref(''),
  notice = ref('')
const dialog = ref<HTMLDialogElement>(),
  errorBox = ref<HTMLElement>()
const mode = ref<Mode>('create'),
  selected = ref<UserAccount | null>(null)
const email = ref(''),
  fullName = ref(''),
  password = ref(''),
  role = ref<Role>('EMPLOYEE'),
  formError = ref('')
const formErrorBox = ref<HTMLElement>()
const titles = {
  create: 'Create account',
  role: 'Change role',
  password: 'Reset password',
  active: 'Change account access',
}
const title = computed(() => titles[mode.value])

async function load() {
  loading.value = true
  error.value = ''
  try {
    data.value = await request<Page>(`/users?page=${page.value}`)
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : 'Unable to load accounts.'
    await nextTick()
    errorBox.value?.focus()
  } finally {
    loading.value = false
  }
}
async function go(next: number) {
  page.value = next
  await load()
}
async function open(value: Mode, user: UserAccount | null = null) {
  mode.value = value
  selected.value = user
  email.value = fullName.value = password.value = formError.value = ''
  role.value = user?.role ?? 'EMPLOYEE'
  await nextTick()
  dialog.value?.showModal()
}
function close() {
  dialog.value?.close()
  password.value = ''
  selected.value = null
}
async function save() {
  busy.value = true
  formError.value = ''
  notice.value = ''
  try {
    const user = selected.value
    if (mode.value === 'create')
      await send('/users', 'POST', {
        email: email.value,
        fullName: fullName.value,
        password: password.value,
        role: role.value,
      })
    else if (user) {
      if (mode.value === 'role') await send(`/users/${user.id}/role`, 'PUT', { role: role.value })
      if (mode.value === 'active')
        await send(`/users/${user.id}/active`, 'PUT', { isActive: !user.isActive })
      if (mode.value === 'password')
        await send(`/users/${user.id}/reset-password`, 'POST', { password: password.value })
      if (
        user.id === session.value?.user.id &&
        (mode.value === 'password' || (mode.value === 'role' && role.value !== user.role))
      )
        setSession(null)
    }
    close()
    notice.value = 'Account saved.'
    if (session.value) await load()
  } catch (cause) {
    formError.value = cause instanceof Error ? cause.message : 'Unable to save account.'
    await nextTick()
    formErrorBox.value?.focus()
  } finally {
    busy.value = false
  }
}
onMounted(load)
</script>

<template>
  <section class="page-heading heading-row">
    <div>
      <p class="eyebrow">SYSTEM ADMINISTRATION</p>
      <h1>User accounts</h1>
      <p class="muted">Manage who can sign in and what they can access.</p>
    </div>
    <button
      class="primary"
      :disabled="loading || busy"
      @click="open('create')"
    >
      Create account
    </button>
  </section>
  <div
    v-if="error"
    ref="errorBox"
    class="error"
    role="alert"
    tabindex="-1"
  >
    {{ error }}
    <button
      type="button"
      :disabled="loading"
      @click="load"
    >
      Try again
    </button>
  </div>
  <p
    v-if="notice"
    class="notice"
    role="status"
  >
    {{ notice }}
  </p>
  <section
    class="card users-card"
    aria-labelledby="accounts-title"
    :aria-busy="loading"
  >
    <div class="list-heading">
      <h2 id="accounts-title">
        All accounts <span class="count">{{ data.total }}</span>
      </h2>
      <span class="muted">{{ loading ? 'Loading accounts…' : 'One role per account' }}</span>
    </div>
    <p
      v-if="!loading && !data.items.length"
      class="empty"
    >
      No accounts on this page.
    </p>
    <ul class="user-list">
      <li
        v-for="user in data.items"
        :key="user.id"
        class="user-row"
      >
        <div class="user-identity">
          <span
            class="avatar"
            aria-hidden="true"
            >{{ user.fullName.slice(0, 1).toUpperCase() }}</span
          >
          <div>
            <strong
              >{{ user.fullName }}
              <span
                v-if="user.id === session?.user.id"
                class="muted you"
                >(you)</span
              ></strong
            >
            <p>{{ user.email }}</p>
          </div>
        </div>
        <span class="role-badge">{{ roles[user.role] }}</span
        ><span
          class="account-status"
          :class="{ inactive: !user.isActive }"
          ><span aria-hidden="true"></span>{{ user.isActive ? 'Active' : 'Inactive' }}</span
        >
        <div class="row-actions">
          <button
            :aria-label="`Change role for ${user.fullName}`"
            :disabled="loading || busy"
            @click="open('role', user)"
          >
            Role</button
          ><button
            :aria-label="`Reset password for ${user.fullName}`"
            :disabled="loading || busy"
            @click="open('password', user)"
          >
            Reset password</button
          ><button
            :aria-label="`${user.isActive ? 'Disable' : 'Enable'} ${user.fullName}`"
            :disabled="loading || busy"
            @click="open('active', user)"
          >
            {{ user.isActive ? 'Disable' : 'Enable' }}
          </button>
        </div>
      </li>
    </ul>
    <div class="pagination">
      <span class="muted"
        >Page {{ data.page }} of {{ Math.max(1, Math.ceil(data.total / 25)) }}</span
      >
      <div class="actions">
        <button
          :disabled="loading || busy || page <= 1"
          @click="go(page - 1)"
        >
          Previous</button
        ><button
          :disabled="loading || busy || page * 25 >= data.total"
          @click="go(page + 1)"
        >
          Next
        </button>
      </div>
    </div>
  </section>
  <dialog
    ref="dialog"
    aria-labelledby="editor-title"
    @cancel="busy ? $event.preventDefault() : close()"
    @close="password = ''"
  >
    <form @submit.prevent="save">
      <h2 id="editor-title">{{ title }}</h2>
      <p
        v-if="selected"
        class="muted"
      >
        {{ selected.fullName }} · {{ selected.email }}
      </p>
      <div
        v-if="formError"
        ref="formErrorBox"
        class="error"
        role="alert"
        tabindex="-1"
      >
        {{ formError }}
      </div>
      <template v-if="mode === 'create'"
        ><label for="user-name">Full name</label
        ><input
          id="user-name"
          v-model.trim="fullName"
          autocomplete="name"
          required
          maxlength="200" /><label for="user-email">Email address</label
        ><input
          id="user-email"
          v-model.trim="email"
          type="email"
          autocomplete="off"
          required
          maxlength="256"
      /></template>
      <template v-if="mode === 'create' || mode === 'role'"
        ><label for="user-role">Role</label
        ><select
          id="user-role"
          v-model="role"
        >
          <option
            v-for="(label, value) in roles"
            :key="value"
            :value="value"
          >
            {{ label }}
          </option>
        </select>
        <p
          v-if="mode === 'role'"
          class="form-help"
        >
          Changing the role signs this user out of existing sessions.
        </p></template
      >
      <template v-if="mode === 'create' || mode === 'password'"
        ><label for="user-password">{{
          mode === 'create' ? 'Initial password' : 'New password'
        }}</label
        ><input
          id="user-password"
          v-model="password"
          type="password"
          autocomplete="new-password"
          required
          minlength="10"
          maxlength="128"
          aria-describedby="account-password-help"
        />
        <p
          id="account-password-help"
          class="form-help"
        >
          10–128 characters, with uppercase, lowercase, a number and a symbol. Share it with the
          account owner securely.
        </p></template
      >
      <p v-if="mode === 'active'">
        {{
          selected?.isActive
            ? 'Disable sign-in and end existing sessions for this account?'
            : 'Allow this account to sign in again?'
        }}
      </p>
      <p
        v-if="mode === 'password'"
        class="form-help"
      >
        Existing sessions will end. The account owner can sign in using the new password.
      </p>
      <div class="actions dialog-actions">
        <button
          type="button"
          :disabled="busy"
          @click="close"
        >
          Cancel</button
        ><button
          class="primary"
          :disabled="busy"
        >
          {{
            busy
              ? 'Saving…'
              : mode === 'active'
                ? selected?.isActive
                  ? 'Disable account'
                  : 'Enable account'
                : 'Save account'
          }}
        </button>
      </div>
    </form>
  </dialog>
</template>

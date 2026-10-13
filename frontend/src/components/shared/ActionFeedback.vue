<template>
  <div class="space-y-3">
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="message" role="status" class="success">{{ message }}</p>
    <p v-if="busy" role="status" class="muted">Đang xử lý…</p>
  </div>
</template>
<script setup>
defineProps({ error: String, message: String, busy: Boolean });
</script>

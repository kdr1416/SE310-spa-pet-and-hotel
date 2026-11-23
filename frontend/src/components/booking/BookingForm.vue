<template>
  <MockBookingForm v-if="IS_MOCK_ENABLED" @created="$emit('created', $event)" />
  <LiveSpaBookingForm v-else @created="$emit('created', $event)" />
</template>
<script setup>
import { IS_MOCK_ENABLED } from "@/services/api/httpClient";
import MockBookingForm from "./MockBookingForm.vue";
import LiveSpaBookingForm from "./LiveSpaBookingForm.vue";
defineEmits(["created"]);
</script>

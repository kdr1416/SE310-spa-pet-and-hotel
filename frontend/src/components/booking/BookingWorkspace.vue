<template><MockBookingWorkspace v-if="IS_MOCK_ENABLED" /><LiveBookingWorkspace v-else /></template>
<script setup>
import { IS_MOCK_ENABLED } from "@/services/api/httpClient";
import MockBookingWorkspace from "./MockBookingWorkspace.vue";
import LiveBookingWorkspace from "./LiveBookingWorkspace.vue";
</script>

<template><MockStaffCareTasksView v-if="IS_MOCK_ENABLED" /><LiveSpaCareTasks v-else /></template>
<script setup>
import { IS_MOCK_ENABLED } from "@/services/api/httpClient";
import MockStaffCareTasksView from "./MockStaffCareTasksView.vue";
import LiveSpaCareTasks from "@/components/care/LiveSpaCareTasks.vue";
</script>

<template>
  <div v-if="auth.can('BOOKING_MANAGE') && booking.trangThai === 'PENDING'" class="space-y-3">
    <div class="flex flex-wrap gap-2">
      <button type="button" class="btn-primary" :disabled="busy" @click="decide('CONFIRMED')">Chấp nhận</button>
      <button type="button" class="btn-danger" :disabled="busy" @click="rejecting = true">Từ chối</button>
    </div>
    <ActionFeedback :busy="busy" :error="error" />
    <Dialog v-model:visible="rejecting" modal header="Xác nhận từ chối lịch hẹn" :style="{ width: '32rem', maxWidth: '95vw' }">
      <form class="space-y-4" @submit.prevent="decide('CANCELLED')">
        <p>Đơn <strong>{{ booking.trackingCode }}</strong> sẽ bị hủy và giải phóng chỗ đã giữ.</p>
        <label class="label">Lý do từ chối *<textarea v-model="reason" class="field" rows="4" required maxlength="1000" /></label>
        <ActionFeedback :error="error" />
        <div class="flex justify-end gap-2">
          <button type="button" class="btn" :disabled="busy" @click="rejecting = false">Quay lại</button>
          <button type="submit" class="btn-danger" :disabled="busy || !reason.trim()">Xác nhận từ chối</button>
        </div>
      </form>
    </Dialog>
  </div>
</template>
<script setup>
import { ref } from 'vue';
import Dialog from 'primevue/dialog';
import { useAuthStore } from '@/stores/auth';
import { bookingService } from '@/services/api/bookingService';
import { useAction } from '@/composables/useAction';
import ActionFeedback from '@/components/shared/ActionFeedback.vue';
const props = defineProps({ booking: { type: Object, required: true } });
const emit = defineEmits(['changed']);
const auth = useAuthStore(), rejecting = ref(false), reason = ref('');
const { busy, error, perform } = useAction();
async function decide(decision) {
  await perform(async () => {
    await bookingService.reviewBooking({ id: props.booking.id, decision, reason: reason.value.trim() });
    rejecting.value = false;
    emit('changed');
  });
}
</script>

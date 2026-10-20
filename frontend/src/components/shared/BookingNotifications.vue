<template>
  <div v-if="auth.can('BOOKING_MANAGE')" class="relative">
    <button class="btn relative" type="button" :aria-expanded="open" aria-controls="new-bookings" @click="open = !open; refresh()">
      <Bell :size="17" aria-hidden="true" /> Đơn mới <span class="rounded-full bg-primary-100 px-2 font-bold">{{ bookings.length }}</span>
    </button>
    <div v-if="open" id="new-bookings" class="absolute right-0 z-40 mt-2 max-h-96 w-80 max-w-[90vw] overflow-auto rounded-2xl border border-app-border bg-white p-4 shadow-lg" @keydown.esc="open = false">
      <h2 class="text-base">Đơn đang chờ xác nhận</h2>
      <p v-if="error" role="alert" class="mt-2 text-sm text-red-800">{{ error }}</p>
      <p v-else-if="!bookings.length" class="muted mt-3">Chưa có đơn mới.</p>
      <RouterLink v-for="b in bookings" :key="b.id" :to="`${auth.isAdmin ? '/admin' : '/staff'}/bookings?code=${encodeURIComponent(b.trackingCode)}`" class="mt-3 block rounded-lg border border-primary-100 p-3 hover:bg-primary-50" @click="open = false">
        <strong>{{ b.trackingCode }}</strong><p class="text-sm">{{ b.khachHang.tenKhachHang }} · {{ weekdayLabel(b.date) }}</p>
      </RouterLink>
    </div>
  </div>
</template>
<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useRouter } from 'vue-router';
import { Bell } from 'lucide-vue-next';
import { useAuthStore } from '@/stores/auth';
import { bookingService } from '@/services/api/bookingService';
import { weekdayLabel } from '@/utils/dates';
const auth = useAuthStore(), router = useRouter(), bookings = ref([]), open = ref(false), error = ref('');
let timer, loading = false;
async function refresh() {
  if (loading || document.hidden) return;
  loading = true;
  try {
    const role = auth.user?.vaiTro;
    await auth.initAuth();
    if (!auth.isAuthenticated) { bookings.value = []; await router.replace('/login'); return; }
    if (role !== auth.user.vaiTro) { open.value = false; await router.replace(auth.isAdmin ? '/admin/overview' : '/staff/overview'); }
    if (auth.can('BOOKING_MANAGE')) {
      const { data } = await bookingService.getWorkspace();
      bookings.value = data.bookings.filter((b) => b.trangThai === 'PENDING');
    } else bookings.value = [];
    error.value = '';
  } catch { error.value = 'Không tải được thông báo. Bấm để thử lại.'; }
  finally { loading = false; }
}
function storageChanged(e) { if (e.key === 'petspa_workspace_update') refresh(); }
onMounted(() => {
  refresh(); timer = setInterval(refresh, 30000);
  document.addEventListener('visibilitychange', refresh);
  window.addEventListener('petspa-workspace-updated', refresh);
  window.addEventListener('storage', storageChanged);
});
onBeforeUnmount(() => {
  clearInterval(timer); document.removeEventListener('visibilitychange', refresh);
  window.removeEventListener('petspa-workspace-updated', refresh); window.removeEventListener('storage', storageChanged);
});
</script>

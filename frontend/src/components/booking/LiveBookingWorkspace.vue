<template>
  <div class="space-y-5" :aria-busy="loading">
    <h1>Lịch hẹn Spa</h1>
    <div v-if="error" role="alert" class="panel text-red-800">{{ error }}</div>
    <p v-if="message" role="status">{{ message }}</p>
    <div class="flex flex-wrap items-end gap-3">
      <label class="label">Trạng thái<select data-testid="status" :value="store.bookingsQuery.trangThai" class="field" :disabled="loading || detailBusy" @change="load({ trangThai: $event.target.value })"><option v-for="state in states" :key="state">{{ state }}</option></select></label>
      <label class="label">Từ ngày<input v-model="from" class="field" type="date" :disabled="detailBusy" /></label>
      <label class="label">Đến ngày<input v-model="to" class="field" type="date" :disabled="detailBusy" /></label>
      <button class="btn" :disabled="loading || detailBusy" @click="load({ tuNgay: from || undefined, denNgay: to || undefined })">Lọc ngày</button>
      <button class="btn" :disabled="loading || detailBusy" @click="load()">Tải lại</button>
      <button class="btn" :aria-pressed="calendar" @click="calendar = !calendar">{{ calendar ? 'Xem danh sách' : 'Xem theo ngày' }}</button>
    </div>
    <p role="status">{{ store.bookingsPage.tongSo }} lịch hẹn · Trang {{ store.bookingsPage.trang }} · Đang hiển thị {{ store.bookings.length }} lịch trên trang này.</p>
    <p v-if="calendar" class="muted">Các ngày dưới đây chỉ gồm dữ liệu của trang đã tải.</p>
    <p v-if="loading" role="status">Đang tải lịch hẹn…</p>
    <p v-else-if="!store.bookings.length" role="status">Không có lịch hẹn trong bộ lọc này.</p>
    <div class="grid gap-4 md:grid-cols-2">
      <article v-for="booking in displayed" :key="booking.maDon" class="panel space-y-3">
        <h2>{{ booking.trackingCode }}</h2><p>{{ booking.date }} · {{ booking.trangThai }}</p>
        <p>{{ booking.khachHang?.tenKhachHang ?? booking.khachHang?.hoTen ?? 'Thông tin khách chưa khả dụng' }}</p>
        <p>{{ booking.thuCung.length }} thú cưng</p>
        <button class="btn" :data-testid="`open-${booking.maDon}`" :disabled="detailBusy" @click="selected = { ...booking }">Chi tiết</button>
      </article>
    </div>
    <div class="flex gap-3">
      <button class="btn" :disabled="loading || detailBusy || store.bookingsPage.trang <= 1" @click="load({ trang: store.bookingsPage.trang - 1 })">Trang trước</button>
      <button class="btn" data-testid="next-page" :disabled="loading || detailBusy || store.bookingsPage.trang * store.bookingsPage.kichThuocTrang >= store.bookingsPage.tongSo" @click="load({ trang: store.bookingsPage.trang + 1 })">Trang sau</button>
    </div>
    <Dialog :visible="!!selected" modal header="Chi tiết đơn" :closable="!detailBusy" :close-on-escape="!detailBusy" :style="{ width: '60rem', maxWidth: '96vw' }" @update:visible="close">
      <LiveSpaBookingDetail v-if="selected" :key="selected.maDon" :booking="selected" @changed="changed" @busy="detailBusy = $event" />
    </Dialog>
  </div>
</template>
<script setup>
import { computed, onMounted, onBeforeUnmount, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import Dialog from "primevue/dialog";
import { useWorkspaceStore } from "@/stores/workspace";
import { bookingService } from "@/services/api/bookingService";
import LiveSpaBookingDetail from "./LiveSpaBookingDetail.vue";
const store = useWorkspaceStore(), route = useRoute(), router = useRouter();
const states = ["PENDING", "CONFIRMED", "IN_PROGRESS", "COMPLETED", "CANCELLED"];
const loading = ref(false), error = ref(""), selected = ref(null), detailBusy = ref(false), from = ref(""), to = ref(""), calendar = ref(false);
const message = ref("");
let sequence = 0, lookupSequence = 0;
watch(() => store.scopeEpoch, () => { sequence++; lookupSequence++; selected.value = null; detailBusy.value = false; loading.value = false; message.value = ""; }, { flush: "sync" });
const displayed = computed(() => calendar.value ? [...store.bookings].sort((a, b) => a.date.localeCompare(b.date) || a.maDon - b.maDon) : store.bookings);
async function load(query = {}) {
  const current = ++sequence; loading.value = true;
  try {
    const response = await store.loadBookings(query); if (current !== sequence) return;
    if (response?.success === false) throw response;
    if (selected.value && !detailBusy.value) { const updated = store.bookings.find(booking => booking.maDon === selected.value.maDon); if (updated) selected.value = { ...updated }; }
    error.value = "";
  } catch (e) { if (current === sequence) error.value = e.message || "Không tải được lịch hẹn."; }
  finally { if (current === sequence) loading.value = false; }
}
async function changed(change = {}) {
  if (change.booking) selected.value = { ...change.booking };
  if (change.invoice && selected.value) selected.value = { ...selected.value, hoaDon: change.invoice };
  if (change.pet && selected.value) selected.value = { ...selected.value, trangThai: change.reloadOnly ? selected.value.trangThai : change.trangThai,
    thuCung: selected.value.thuCung.map(pet => pet.maChiTietDon === change.pet.maChiTietDon ? change.pet : pet) };
  await load(change.trangThai ? { trangThai: change.trangThai } : {});
  if (change.reloadOnly && selected.value && !store.bookings.some(booking => booking.maDon === selected.value.maDon)) {
    selected.value = null;
    message.value = "Đã bàn giao. Đơn không nằm trong trang hiện tại; đổi bộ lọc hoặc trang để xem trạng thái từ server.";
  }
}
async function openNotification(code) {
  if (!code || detailBusy.value) return;
  const current = ++lookupSequence; selected.value = null; loading.value = true;
  try {
    for (let trang = 1; ; trang++) {
      const response = await bookingService.getWorkspace({ trang, kichThuocTrang: 100, trangThai: "PENDING" });
      if (current !== lookupSequence) return;
      if (response.success === false) throw response;
      const found = response.data.bookings.find(booking => booking.trackingCode === code);
      if (found) { selected.value = { ...found }; error.value = ""; return; }
      if (trang * 100 >= response.data.tongSo || !response.data.bookings.length) break;
    }
    message.value = "Không tìm thấy lịch hẹn đang chờ từ thông báo. Đơn có thể đã đổi trạng thái; chọn bộ lọc để kiểm tra lại.";
  } catch (e) {
    if (current === lookupSequence) { error.value = e.message || "Không tải được lịch hẹn từ thông báo."; if (["FORBIDDEN", "UNAUTHENTICATED"].includes(e.errorCode)) store.resetPrivate(); }
  } finally { if (current === lookupSequence) loading.value = false; }
}
watch(() => route.query.code, code => { if (code) openNotification(code); });
function close(visible) { if (!visible && !detailBusy.value) { selected.value = null; lookupSequence++; if (route.query.code) router.replace({ query: { ...route.query, code: undefined } }); } }
function refresh() { if (!detailBusy.value) load(); }
onMounted(() => { window.addEventListener("petspa-workspace-updated", refresh); load(); if (route.query.code) openNotification(route.query.code); });
onBeforeUnmount(() => { sequence++; lookupSequence++; window.removeEventListener("petspa-workspace-updated", refresh); });
</script>

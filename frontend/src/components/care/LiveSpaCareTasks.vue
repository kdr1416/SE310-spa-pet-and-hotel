<template>
  <div class="space-y-5" :aria-busy="loading || actionBusy || logBusy">
    <h1>Công việc chăm sóc</h1>
    <p class="muted">Các bé được server xác nhận thuộc phạm vi công việc của bạn.</p>
    <div v-if="error" ref="errorSummary" role="alert" tabindex="-1" class="panel text-red-800">{{ error }}<ul><li v-for="(item, i) in errors" :key="i">{{ item.message ?? item }}</li></ul></div>
    <p v-if="message" role="status">{{ message }}</p>
    <div class="flex flex-wrap items-end gap-3">
      <label class="label">Trạng thái<select :value="store.careQuery.trangThai ?? ''" class="field" :disabled="locked" @change="load({ trangThai: $event.target.value || undefined })"><option value="">Tất cả công việc đang phục vụ</option><option v-for="state in ['WAITING','IN_PROGRESS','PAUSED','READY']" :key="state">{{ state }}</option></select></label>
      <button class="btn" :disabled="loading || locked" @click="load()">Tải lại</button>
      <p role="status">{{ store.carePage.tongSo }} công việc · Trang {{ store.carePage.trang }}</p>
      <button class="btn" :disabled="loading || locked || store.carePage.trang <= 1" @click="load({ trang: store.carePage.trang - 1 })">Trang trước</button>
      <button class="btn" :disabled="loading || locked || store.carePage.trang * store.carePage.kichThuocTrang >= store.carePage.tongSo" @click="load({ trang: store.carePage.trang + 1 })">Trang sau</button>
    </div>
    <p v-if="!loading && !store.careTasks.length" role="status">Không có công việc trong bộ lọc này.</p>
    <div class="grid gap-4 md:grid-cols-2">
      <article v-for="pet in store.careTasks" :key="pet.maChiTietDon" class="panel space-y-3">
        <h2>{{ pet.tenThuCung }}</h2><p>{{ pet.trangThai }} · {{ pet.canNangChot ?? 'Chưa chốt' }} kg</p>
        <p>{{ pet.dichVu.map(service => service.tenDichVu).join(', ') }}</p>
        <p class="muted">Phụ trách: {{ pet.tenNhanVienPhuTrach ?? 'Chưa có dữ liệu' }}</p>
        <p v-if="unsupported(pet)">Hotel/Combo chưa hỗ trợ thao tác ở màn hình này.</p>
        <button v-else class="btn" :data-testid="`open-${pet.maChiTietDon}`" :disabled="locked" @click="select(pet)">Mở công việc</button>
      </article>
    </div>
    <section v-if="selected" class="panel space-y-5" aria-labelledby="care-selected">
      <div class="flex justify-between gap-3"><h2 id="care-selected">{{ selected.tenThuCung }} · {{ selected.trangThai }}</h2><button class="btn" :disabled="locked" @click="selected = null">Đóng</button></div>
      <p v-if="actionUncertain || logUncertain" role="status">Chưa rõ kết quả. Thử lại cùng yêu cầu để tránh ghi trùng.</p>
      <form v-if="auth.can('CARE_UPDATE') && actions.length" data-testid="action-form" class="space-y-3" @submit.prevent="perform">
        <fieldset :disabled="locked" class="grid gap-3 sm:grid-cols-2">
          <legend>Cập nhật trạng thái</legend>
          <label class="label">Hành động<select v-model="action" data-testid="action" class="field"><option v-for="value in actions" :key="value">{{ value }}</option></select></label>
          <label class="label">Ghi chú / lý do tạm dừng<textarea v-model="note" class="field" maxlength="2000" :required="action === 'PAUSE'" /></label>
          <label v-if="action === 'READY'" class="label">Ảnh thành phẩm *<input type="file" accept="image/jpeg,image/png,image/webp" @change="setPhoto($event, false)" /></label>
        </fieldset>
        <button class="btn-primary" :disabled="actionBusy || logBusy || logUncertain">{{ actionUncertain ? 'Thử lại yêu cầu' : 'Xác nhận hành động' }}</button>
      </form>
      <form v-if="auth.can('PHOTO_UPLOAD')" data-testid="log-form" class="space-y-3" @submit.prevent="addLog">
        <fieldset :disabled="locked" class="grid gap-3 sm:grid-cols-2"><legend>Thêm nhật ký riêng</legend>
          <label class="label">Ghi chú<textarea v-model="logNote" data-testid="log-note" class="field" maxlength="2000" /></label>
          <label class="label">Ảnh nhật ký<input type="file" accept="image/jpeg,image/png,image/webp" @change="setPhoto($event, true)" /></label>
        </fieldset>
        <button class="btn" :disabled="actionBusy || logBusy || actionUncertain">{{ logUncertain ? 'Thử lại nhật ký' : 'Lưu nhật ký' }}</button>
      </form>
      <article v-for="log in selected.nhatKy" :key="log.maNhatKy" class="space-y-2"><p>{{ log.ghiChu }} · {{ log.ghiNhanAt }}</p><div v-if="log.anhUrl" class="h-48"><StoredPhoto :blob="log.anhUrl" alt="Ảnh nhật ký chăm sóc" /></div></article>
    </section>
  </div>
</template>
<script setup>
import { computed, nextTick, onMounted, onBeforeUnmount, ref } from "vue";
import { useWorkspaceStore } from "@/stores/workspace";
import { useAuthStore } from "@/stores/auth";
import { bookingService } from "@/services/api/bookingService";
import { useSpaCommand } from "@/composables/useSpaCommand";
import { validateImage } from "@/utils/bookingRules";
import StoredPhoto from "@/components/shared/StoredPhoto.vue";
const store = useWorkspaceStore(), auth = useAuthStore();
const selected = ref(null), loading = ref(false), error = ref(""), errors = ref([]), errorSummary = ref(null), message = ref("");
const action = ref(""), note = ref(""), photo = ref(null), logNote = ref(""), logPhoto = ref(null);
const actionCommand = useSpaCommand(), logCommand = useSpaCommand();
const { busy: actionBusy, uncertain: actionUncertain } = actionCommand;
const { busy: logBusy, uncertain: logUncertain } = logCommand;
const locked = computed(() => actionBusy.value || logBusy.value || actionUncertain.value || logUncertain.value);
const actions = computed(() => ({ WAITING: ["START"], PAUSED: ["RESUME"], IN_PROGRESS: ["PAUSE", "READY"] }[selected.value?.trangThai] ?? []));
const unsupported = pet => pet.maLoaiPhong != null || pet.dichVu.some(service => service.maCombo != null);
async function showError(e) { error.value = e.message || "Không thể xử lý yêu cầu."; errors.value = e.errors ?? []; await nextTick(); errorSummary.value?.focus(); }
function select(pet) { selected.value = { ...pet }; action.value = ({ WAITING: "START", PAUSED: "RESUME", IN_PROGRESS: "PAUSE" })[pet.trangThai] ?? ""; note.value = ""; photo.value = null; logNote.value = ""; logPhoto.value = null; error.value = ""; }
async function load(query = {}) {
  loading.value = true;
  try {
    const response = await store.loadCareTasks(query); if (response?.success === false) throw response;
    if (selected.value && !locked.value) { const current = store.careTasks.find(pet => pet.maChiTietDon === selected.value.maChiTietDon); if (current) { selected.value = { ...current }; action.value = actions.value[0] ?? ""; } else selected.value = null; }
  } catch (e) { await showError(e); } finally { loading.value = false; }
}
async function handleFailure(e) { await showError(e); if (["FORBIDDEN", "INVALID_TRANSITION", "NOT_FOUND"].includes(e.errorCode)) await load(); }
async function perform() {
  if (!selected.value || actionBusy.value || logBusy.value) return;
  try {
    if (!actionUncertain.value && action.value === "PAUSE" && !note.value.trim()) throw new Error("Nhập lý do tạm dừng.");
    if (!actionUncertain.value && action.value === "READY" && (!(photo.value instanceof File) || photo.value.size === 0)) throw new Error("Chọn ảnh thành phẩm hợp lệ.");
    await actionCommand.run({ maChiTietDon: selected.value.maChiTietDon, hanhDong: action.value, ghiChu: note.value || undefined, anhThanhPham: photo.value ?? undefined }, bookingService.performCareAction);
    message.value = "Đã cập nhật trạng thái."; error.value = ""; await load();
  } catch (e) { await handleFailure(e); }
}
async function addLog() {
  if (!selected.value || actionBusy.value || logBusy.value) return;
  try {
    if (!logUncertain.value && !logNote.value.trim() && !logPhoto.value) throw new Error("Nhập ghi chú hoặc chọn ảnh nhật ký.");
    await logCommand.run({ maChiTietDon: selected.value.maChiTietDon, ghiChu: logNote.value || undefined, anh: logPhoto.value ?? undefined }, bookingService.addCareLog);
    logNote.value = ""; logPhoto.value = null; error.value = ""; message.value = "Đã lưu nhật ký."; await load();
  } catch (e) { await handleFailure(e); }
}
function setPhoto(event, log) { try { const file = event.target.files?.[0]; if (file) validateImage(file); if (log) logPhoto.value = file ?? null; else photo.value = file ?? null; } catch (e) { event.target.value = ""; showError(e); } }
function refresh() { if (!locked.value) load(); }
onMounted(() => { load(); window.addEventListener("petspa-workspace-updated", refresh); });
onBeforeUnmount(() => window.removeEventListener("petspa-workspace-updated", refresh));
</script>


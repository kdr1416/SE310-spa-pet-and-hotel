<template>
  <form class="space-y-5" :aria-busy="busy" @submit.prevent="submit">
    <p class="muted">Đặt dịch vụ Spa. Hotel và Combo sẽ được hỗ trợ sau.</p>
    <div v-if="error" ref="errorSummary" class="panel text-red-800" role="alert" tabindex="-1">
      {{ error }}<ul v-if="errors.length"><li v-for="(item, i) in errors" :key="i">{{ item.message ?? item }}</li></ul>
    </div>
    <fieldset class="panel grid gap-4 sm:grid-cols-2" :disabled="busy || uncertain">
      <legend>Thông tin liên hệ</legend>
      <label class="label">Họ và tên *<input v-model="contact.hoTen" data-testid="name" class="field" required maxlength="100" autocomplete="name" /></label>
      <label class="label">Số điện thoại *<input v-model="contact.soDienThoai" data-testid="phone" class="field" type="tel" required autocomplete="tel" pattern="[0-9]{10}" /></label>
      <label class="label">Email *<input v-model="contact.email" data-testid="email" class="field" type="email" required autocomplete="email" /></label>
    </fieldset>
    <fieldset v-for="(pet, i) in pets" :key="pet.key" class="panel grid gap-4 sm:grid-cols-2" :disabled="busy || uncertain">
      <legend>Thú cưng {{ i + 1 }}</legend>
      <label class="label">Tên bé *<input v-model="pet.tenThuCung" data-testid="pet-name" class="field" required maxlength="100" /></label>
      <label class="label">Loài *<select v-model="pet.loaiPet" class="field"><option value="CHO">Chó</option><option value="MEO">Mèo</option></select></label>
      <label class="label">Cân nặng khai báo (kg) *<input v-model.number="pet.canNangKhaiBao" data-testid="weight" class="field" type="number" min="0.1" max="150" step="0.1" required /></label>
      <label class="label">Dịch vụ *<select v-model.number="pet.serviceId" data-testid="service" class="field" required><option value="">Chọn dịch vụ</option><option v-for="service in services" :key="service.id" :value="service.id">{{ service.tenDichVu }}</option></select></label>
      <p class="muted">Ước tính: {{ estimate(pet) }}. Giá được chốt khi tiếp nhận.</p>
      <label class="label">Ghi chú dị ứng<input v-model="pet.ghiChuDiUng" class="field" maxlength="1000" /></label>
      <label class="label">Ảnh giấy tiêm chủng (nếu có)<input type="file" accept="image/jpeg,image/png,image/webp" @change="setFile(pet, $event)" /></label>
      <button v-if="pets.length > 1" type="button" class="btn" @click="pets.splice(i, 1)">Xóa bé {{ i + 1 }}</button>
    </fieldset>
    <button type="button" class="btn" :disabled="busy || uncertain" @click="pets.push(newPet())">Thêm thú cưng</button>
    <fieldset class="panel grid gap-4 sm:grid-cols-2" :disabled="busy || uncertain">
      <legend>Ngày và ca tiếp nhận</legend>
      <label class="label">Ngày nhận *<input v-model="date" data-testid="date" class="field" type="date" :min="today" :max="lastDay" required /></label>
      <label class="label">Ca *<select v-model.number="selectedMaCa" data-testid="shift" class="field" required :disabled="loadingOptions"><option value="">Chọn ca</option><option v-for="shift in shifts" :key="shift.maCa" :value="shift.maCa" :disabled="!bookable(shift)">{{ shift.tenCa }} · {{ shift.gioBatDau }}–{{ shift.gioKetThuc }}</option></select></label>
      <p v-if="loadingOptions" role="status">Đang tải ca…</p>
      <button type="button" data-testid="availability" class="btn" :disabled="checking || loadingOptions" @click="check">{{ checking ? 'Đang kiểm tra…' : 'Kiểm tra chỗ' }}</button>
      <p role="status">{{ availability ? 'Có thể gửi yêu cầu đặt lịch. Chỗ được xác nhận khi lưu thành công.' : 'Kiểm tra chỗ sau khi chọn dịch vụ và ca.' }}</p>
    </fieldset>
    <p v-if="uncertain" role="status">Chưa xác định kết quả lưu. Thử lại cùng yêu cầu hoặc tra cứu trước khi đặt yêu cầu khác.</p>
    <button class="btn-primary" type="submit" :disabled="busy || checking">{{ busy ? 'Đang gửi…' : uncertain ? 'Thử lại yêu cầu' : 'Gửi yêu cầu đặt lịch' }}</button>
  </form>
</template>
<script setup>
import { computed, nextTick, onMounted, ref, watch } from "vue";
import { catalogService } from "@/services/api/catalogService";
import { callOperation } from "@/services/api/operationClient";
import { createCustomerBooking } from "@/services/api/bookingService";
import { estimatePet } from "@/utils/pricing";
import { validateImage } from "@/utils/bookingRules";

const emit = defineEmits(["created"]);
function vnDate() { return new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Ho_Chi_Minh", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date()); }
const today = vnDate(), last = new Date(`${today}T00:00:00Z`); last.setUTCDate(last.getUTCDate() + 90);
const lastDay = last.toISOString().slice(0, 10);
const date = ref(today), selectedMaCa = ref(""), shifts = ref([]), catalog = ref({ services: [], combos: [], rooms: [] });
const contact = ref({ hoTen: "", soDienThoai: "", email: "" });
const newPet = () => ({ key: crypto.randomUUID(), tenThuCung: "", loaiPet: "CHO", canNangKhaiBao: null, serviceId: "", ghiChuDiUng: "", vaccinationFile: null });
const pets = ref([newPet()]), error = ref(""), errors = ref([]), errorSummary = ref(null);
const busy = ref(false), checking = ref(false), uncertain = ref(false), loadingOptions = ref(false), availability = ref(false);
let optionsSequence = 0, inputSequence = 0, command = null;
const services = computed(() => catalog.value.services.filter(item => item.isActive !== false && item.loaiDichVu !== "SPA_ADVANCED"));
const selected = computed(() => shifts.value.find(shift => shift.maCa === selectedMaCa.value));
function bookable(shift) {
  if (!shift.moNhanKhach || !Number.isInteger(shift.maKhungGio)) return false;
  const nowTime = new Intl.DateTimeFormat("en-GB", { timeZone: "Asia/Ho_Chi_Minh", hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false }).format(new Date());
  return date.value > vnDate() || (date.value === vnDate() && shift.gioKetThuc > nowTime);
}
async function showError(e) { error.value = e.message || "Không thể xử lý yêu cầu."; errors.value = e.errors ?? []; await nextTick(); errorSummary.value?.focus(); }
async function loadOptions() {
  const sequence = ++optionsSequence; selectedMaCa.value = ""; shifts.value = []; loadingOptions.value = true;
  try { const response = await catalogService.getBookingOptions({ ngay: date.value }); if (sequence === optionsSequence) { if (!response.success) throw response; shifts.value = response.data.ca; } }
  catch (e) { if (sequence === optionsSequence) await showError(e); }
  finally { if (sequence === optionsSequence) loadingOptions.value = false; }
}
watch(date, loadOptions);
watch([date, selectedMaCa, pets, contact], () => { inputSequence++; availability.value = false; if (!uncertain.value) command = null; }, { deep: true, flush: "sync" });
function demand() {
  if (!selected.value || !bookable(selected.value)) throw new Error("Chọn ca còn nhận khách.");
  if (!pets.value.length || pets.value.some(pet => !pet.serviceId || !(pet.canNangKhaiBao > 0))) throw new Error("Nhập cân nặng và chọn dịch vụ cho từng bé.");
  return { maKhungGio: selected.value.maKhungGio, ngayTra: date.value,
    thuCung: pets.value.map(pet => ({ ...pet, dichVu: [{ maDichVu: pet.serviceId, maKhungGio: selected.value.maKhungGio }] })) };
}
async function check() {
  const sequence = inputSequence; error.value = ""; checking.value = true; availability.value = false;
  try { const response = await callOperation("checkAvailability", demand(), true); if (sequence !== inputSequence) return; if (!response.success || !response.data.coTheDat) throw new Error(response.data?.lyDo || response.message || "Chưa thể đặt ca này."); availability.value = true; }
  catch (e) { if (sequence === inputSequence) await showError(e); }
  finally { checking.value = false; }
}
async function submit() {
  if (busy.value) return;
  error.value = ""; errors.value = [];
  try {
    if (!command) {
      if (!availability.value) throw new Error("Kiểm tra lại chỗ trước khi gửi.");
      command = { ...demand(), lienHe: { ...contact.value }, requestId: crypto.randomUUID() };
    }
    busy.value = true;
    const response = await createCustomerBooking(command); if (!response.success) throw response;
    uncertain.value = false; command = null; emit("created", response.data);
  } catch (e) {
    if (busy.value && (!e.errorCode || e.errorCode === "TEMPORARY_FAILURE")) uncertain.value = true;
    else if (busy.value) { uncertain.value = false; command = null; availability.value = false; if (["CAPACITY_EXCEEDED", "INVALID_TRANSITION"].includes(e.errorCode)) await loadOptions(); }
    await showError(e);
  } finally { busy.value = false; }
}
function estimate(pet) { try { return new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" }).format(estimatePet({ type: "SPA", serviceId: pet.serviceId, weight: pet.canNangKhaiBao }, date.value, catalog.value).amount); } catch { return "Chưa có giá"; } }
function setFile(pet, event) {
  const file = event.target.files?.[0];
  if (!file) { pet.vaccinationFile = null; return; }
  try { validateImage(file); pet.vaccinationFile = file; }
  catch (e) { pet.vaccinationFile = null; event.target.value = ""; showError(e); }
}
onMounted(async () => { loadOptions(); try { const response = await catalogService.catalog(); if (response.success === false) throw response; catalog.value = response.data; } catch (e) { await showError(e); } });
</script>

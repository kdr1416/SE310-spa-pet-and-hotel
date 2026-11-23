<template>
  <div class="space-y-5">
    <h2>{{ booking.trackingCode }} · {{ booking.trangThai }}</h2>
    <div v-if="error" ref="errorSummary" role="alert" tabindex="-1" class="panel text-red-800">{{ error }}<ul><li v-for="(item, i) in errors" :key="i">{{ item.message ?? item }}</li></ul></div>
    <p v-if="message" role="status">{{ message }}</p>
    <p v-if="unsupported" class="muted">Hotel/Combo chưa hỗ trợ thao tác ở màn hình này.</p>
    <form v-if="!unsupported && booking.trangThai === 'PENDING' && auth.can('BOOKING_APPROVE')" class="panel space-y-3" @submit.prevent="review('CONFIRMED')">
      <label class="label">Lý do từ chối (nếu từ chối)<textarea v-model="rejection" class="field" :disabled="locked" maxlength="1000" /></label>
      <button class="btn-primary" :disabled="busy || intakeUncertain" type="submit">Xác nhận đơn</button>
      <button class="btn ml-3" :disabled="busy || intakeUncertain" type="button" @click="review('CANCELLED')">Từ chối đơn</button>
    </form>
    <p v-if="reviewUncertain || intakeUncertain" role="status">Chưa rõ kết quả. Thử lại cùng yêu cầu trước khi thao tác khác.</p>
    <article v-for="pet in booking.thuCung" :key="pet.maChiTietDon" class="panel space-y-3">
      <h3>{{ pet.tenThuCung }} · {{ pet.trangThai }}</h3>
      <p>{{ pet.dichVu.map(service => service.tenDichVu).join(', ') }}</p>
      <p class="muted">Phụ trách: {{ pet.tenNhanVienPhuTrach ?? 'Chưa có dữ liệu' }} · Cân nặng chốt: {{ pet.canNangChot ?? 'Chưa tiếp nhận' }}</p>
      <button v-if="!unsupported && ['CONFIRMED','IN_PROGRESS'].includes(booking.trangThai) && pet.trangThai === 'NOT_ARRIVED' && auth.can('CHECKIN_PERFORM')" class="btn" :data-testid="`checkin-${pet.maChiTietDon}`" :disabled="locked" @click="openIntake(pet)">Tiếp nhận {{ pet.tenThuCung }}</button>
    </article>
    <form v-if="intakePet" data-testid="checkin-form" class="panel space-y-4" :aria-busy="busy || loading" @submit.prevent="checkIn">
      <h3>Tiếp nhận {{ intakePet.tenThuCung }}</h3>
      <button type="button" class="btn" :disabled="locked || loading" @click="loadReferences">Tải lại ca và danh mục</button>
      <p v-if="loading" role="status">Đang tải dữ liệu tiếp nhận…</p>
      <p v-if="!loading && !shifts.length" role="status">Không có ca thao tác hiện tại. Chưa thể tiếp nhận bình thường.</p>
      <p v-if="!loading && maCa && !employees.length" role="status">Không có nhân viên được duyệt trong ca này.</p>
      <fieldset :disabled="locked || loading" class="grid gap-4 sm:grid-cols-2">
        <legend>Thông tin thực tế, do người tiếp nhận xác nhận</legend>
        <label class="label">Ca thao tác hiện tại *<select v-model.number="maCa" class="field" required @change="loadEmployees"><option value="">Chọn ca</option><option v-for="shift in shifts" :key="shift.maCa" :value="shift.maCa">{{ shift.tenCa }}</option></select></label>
        <label class="label">Nhân viên phụ trách *<select v-model.number="form.maNhanVien" data-testid="employee" class="field" required><option value="">Chọn nhân viên</option><option v-for="employee in employees" :key="employee.maNhanVien" :value="employee.maNhanVien">{{ employee.tenNhanVien }}</option></select></label>
        <label class="label">Cân nặng thực tế (kg) *<input v-model.number="form.canNangThucTe" data-testid="weight" class="field" type="number" min="0.1" max="150" step="0.1" required /></label>
        <label class="label">Tình trạng lông *<select v-model="form.tinhTrangLong" data-testid="fur" class="field" required><option value="">Chọn tình trạng lông</option><option v-for="item in fur" :key="item.ma" :value="item.ma">{{ item.ten }}</option></select></label>
        <label class="label">Sức khỏe lúc tiếp nhận *<textarea v-model="form.ghiChuSucKhoe" data-testid="health" class="field" maxlength="1000" required /></label>
        <label class="label">Quyết định giá *<select v-model="form.quyetDinhGia" data-testid="price" class="field" required><option value="">Chọn sau khi trao đổi với khách</option><option value="SIZE_THUC_TE">Tính theo size thực tế</option><option value="THU_GON">Thu gọn dịch vụ</option></select></label>
        <label v-if="form.quyetDinhGia === 'THU_GON'" class="label">Nội dung thu gọn *<textarea v-model="form.noiDungThuGon" class="field" required maxlength="1000" /></label>
        <label class="label">Ảnh tiếp nhận *<input data-testid="intake-photo" type="file" accept="image/jpeg,image/png,image/webp" required @change="setPhoto" /></label>
        <label class="label">Mã hồ sơ tiêm chủng (nếu có)<input v-model.number="form.maHoSoTiemChung" class="field" type="number" min="1" /></label>
        <label class="flex items-start gap-2"><input v-model="form.xacNhanGiayHopLe" type="checkbox" />Đã kiểm tra giấy tiêm chủng hợp lệ</label>
        <label class="flex items-start gap-2"><input v-model="form.xacNhanKhach" data-testid="consent" type="checkbox" required />Khách đã đồng thuận với dịch vụ và quyết định giá</label>
      </fieldset>
      <button type="submit" class="btn-primary" :disabled="busy || loading || reviewUncertain">{{ intakeUncertain ? 'Thử lại yêu cầu tiếp nhận' : 'Xác nhận tiếp nhận' }}</button>
    </form>
  </div>
</template>
<script setup>
import { computed, nextTick, ref } from "vue";
import { useAuthStore } from "@/stores/auth";
import { bookingService } from "@/services/api/bookingService";
import { catalogService } from "@/services/api/catalogService";
import { useSpaCommand } from "@/composables/useSpaCommand";
import { validateImage } from "@/utils/bookingRules";
const props = defineProps({ booking: { type: Object, required: true } }), emit = defineEmits(["changed", "busy"]);
const auth = useAuthStore(), intake = useSpaCommand(), approval = useSpaCommand();
const { busy: intakeBusy, uncertain: intakeUncertain } = intake, { busy: reviewBusy, uncertain: reviewUncertain } = approval;
const busy = computed(() => intakeBusy.value || reviewBusy.value), locked = computed(() => busy.value || intakeUncertain.value || reviewUncertain.value);
const unsupported = computed(() => props.booking.thuCung.some(pet => pet.maLoaiPhong != null || pet.dichVu.some(service => service.maCombo != null)));
const error = ref(""), errors = ref([]), errorSummary = ref(null), message = ref(""), rejection = ref("");
const intakePet = ref(null), shifts = ref([]), fur = ref([]), employees = ref([]), maCa = ref(""), loading = ref(false);
let day = "", employeeSequence = 0;
const blank = () => ({ canNangThucTe: null, tinhTrangLong: "", maNhanVien: "", ghiChuSucKhoe: "", quyetDinhGia: "", noiDungThuGon: "", xacNhanKhach: false, xacNhanGiayHopLe: false, maHoSoTiemChung: "", anhTiepNhan: null });
const form = ref(blank());
function setPhoto(event) {
  const file = event.target.files?.[0];
  try { if (file) validateImage(file); form.value.anhTiepNhan = file ?? null; }
  catch (e) { form.value.anhTiepNhan = null; event.target.value = ""; showError(e); }
}
async function showError(e) { error.value = e.message || "Không thể xử lý yêu cầu."; errors.value = e.errors ?? []; await nextTick(); errorSummary.value?.focus(); }
async function loadEmployees() {
  const sequence = ++employeeSequence; employees.value = []; form.value.maNhanVien = "";
  if (!maCa.value) return;
  try { const response = await bookingService.getAssignmentCandidates({ ngay: day, maCa: maCa.value }); if (sequence !== employeeSequence) return; if (!response.success) throw response; employees.value = response.data; }
  catch (e) { if (sequence === employeeSequence) await showError(e); }
}
async function loadReferences() {
  loading.value = true;
  try {
    day = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Ho_Chi_Minh", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
    const time = new Intl.DateTimeFormat("en-GB", { timeZone: "Asia/Ho_Chi_Minh", hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false }).format(new Date());
    const [options, conditions] = await Promise.all([catalogService.getBookingOptions({ ngay: day }), catalogService.getFurConditions()]);
    if (!options.success) throw options; if (!conditions.success) throw conditions;
    shifts.value = options.data.ca.filter(shift => shift.gioBatDau <= time && time < shift.gioKetThuc);
    fur.value = conditions.data.filter(item => item.dangHoatDong);
    maCa.value = shifts.value.length === 1 ? shifts.value[0].maCa : "";
    await loadEmployees();
  } catch (e) { shifts.value = []; fur.value = []; employees.value = []; maCa.value = ""; await showError(e); }
  finally { loading.value = false; }
}
async function openIntake(pet) { intakePet.value = { ...pet }; form.value = blank(); error.value = ""; await loadReferences(); }
async function checkIn() {
  if (!intakePet.value || busy.value || loading.value) return;
  try {
    if (!intakeUncertain.value) {
      const f = form.value;
      if (!maCa.value || !employees.value.some(employee => employee.maNhanVien === f.maNhanVien)) throw new Error("Chọn nhân viên được duyệt trong ca hiện tại.");
      if (!(f.canNangThucTe > 0) || !fur.value.some(item => item.ma === f.tinhTrangLong) || !f.ghiChuSucKhoe.trim() || !["SIZE_THUC_TE", "THU_GON"].includes(f.quyetDinhGia) || !f.xacNhanKhach || !f.anhTiepNhan) throw new Error("Nhập đủ thông tin thực tế, ảnh và đồng thuận của khách.");
      if (f.quyetDinhGia === "THU_GON" && !f.noiDungThuGon.trim()) throw new Error("Nhập nội dung thu gọn.");
    }
    emit("busy", true);
    const f = form.value;
    const response = await intake.run({ ...f, tinhTrangLong: [f.tinhTrangLong], maChiTietDon: intakePet.value.maChiTietDon,
      maHoSoTiemChung: f.maHoSoTiemChung || undefined, xacNhanGiayHopLe: f.xacNhanGiayHopLe || undefined,
      noiDungThuGon: f.quyetDinhGia === "THU_GON" ? f.noiDungThuGon : undefined }, bookingService.checkInPet);
    intakePet.value = null; message.value = "Đã tiếp nhận thú cưng."; error.value = "";
    emit("changed", { trangThai: "IN_PROGRESS", pet: response.data });
  } catch (e) { await showError(e); if (["FORBIDDEN", "INVALID_TRANSITION"].includes(e.errorCode)) emit("changed", { trangThai: props.booking.trangThai }); if (e.errorCode === "VALIDATION_FAILED") await loadReferences(); }
  finally { emit("busy", locked.value); }
}
async function review(decision) {
  if (busy.value) return;
  try {
    if (decision === "CANCELLED" && !rejection.value.trim() && !reviewUncertain.value) throw new Error("Nhập lý do từ chối.");
    emit("busy", true);
    const response = await approval.run({ bookingId: props.booking.maDon, decision, lyDo: rejection.value || undefined }, bookingService.reviewBooking);
    emit("changed", { trangThai: response.data.trangThai, booking: response.data });
    error.value = "";
  } catch (e) { await showError(e); if (["FORBIDDEN", "INVALID_TRANSITION"].includes(e.errorCode)) emit("changed", { trangThai: props.booking.trangThai }); }
  finally { emit("busy", locked.value); }
}
defineExpose({ locked });
</script>

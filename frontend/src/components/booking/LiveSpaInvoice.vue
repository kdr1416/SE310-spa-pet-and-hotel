<template>
  <section class="panel space-y-5" :aria-busy="busy || loading">
    <h3>Hóa đơn và bàn giao</h3>
    <div v-if="error" ref="errorSummary" role="alert" tabindex="-1" class="text-red-800">{{ error }}<ul><li v-for="(item, i) in errors" :key="i">{{ item.message ?? item }}</li></ul></div>
    <p v-if="message" role="status">{{ message }}</p>
    <p v-if="uncertain" role="status">Chưa rõ kết quả. Thử lại đúng yêu cầu, không tạo giao dịch mới.</p>
    <button v-if="canCreate" type="button" data-testid="create-invoice" class="btn-primary" :disabled="busy || loading || payUncertain || handoverUncertain" @click="createInvoice">{{ createUncertain ? 'Thử lại lập hóa đơn' : 'Lập hóa đơn' }}</button>
    <p v-else-if="!invoice" class="muted">Chỉ lập hóa đơn khi các bé đã sẵn sàng bàn giao và bạn có quyền lập hóa đơn.</p>
    <template v-if="invoice">
      <div class="flex flex-wrap items-center justify-between gap-3"><p>Hóa đơn {{ invoice.soHoaDon ?? invoice.maHoaDon }} · {{ invoice.trangThai }}</p><button v-if="auth.can('INVOICE_CREATE')" type="button" data-testid="reload-invoice" class="btn" :disabled="locked || loading" @click="reload">Tải lại hóa đơn</button></div>
      <div class="table-wrap"><table><caption class="sr-only">Các khoản thu từ hóa đơn</caption><thead><tr><th>Khoản thu</th><th>Số lượng</th><th>Thành tiền</th></tr></thead><tbody><tr v-for="line in invoice.dongTien" :key="line.maDongHoaDon"><td>{{ line.tenKhoan }}</td><td>{{ line.soLuong ?? 1 }}</td><td>{{ money(line.thanhTien) }}</td></tr></tbody></table></div>
      <p>Tổng: {{ money(invoice.tongTien) }} · Cọc khấu trừ: {{ money(invoice.cocKhauTru) }} · Còn phải trả: {{ money(invoice.conPhaiTra) }}</p>
      <div v-if="invoice.qr" class="space-y-2"><img :src="invoice.qr.imageUrl" width="180" height="180" alt="QR thanh toán từ hóa đơn" /><p>{{ money(invoice.qr.soTien) }} · {{ invoice.qr.noiDungChuyenKhoan }}</p></div>
      <p v-else class="muted">Chưa có QR thanh toán từ backend. Kiểm tra phương thức thu phù hợp.</p>
      <form v-if="invoice.trangThai === 'UNPAID' && auth.can('INVOICE_COLLECT')" data-testid="payment-form" class="space-y-4" @submit.prevent="pay">
        <fieldset :disabled="locked || loading" class="grid gap-4 sm:grid-cols-2"><legend>Ghi nhận số tiền thực tế</legend>
          <label class="label">Phương thức<select v-model="method" class="field"><option value="CASH">Tiền mặt</option><option value="BANK_TRANSFER">Chuyển khoản</option></select></label>
          <label class="label">Thực nhận (đồng) *<input v-model="received" data-testid="received" type="number" class="field" min="1" step="1" required /></label>
          <label class="label">Đã trả lại khách (đồng) *<input v-model="change" data-testid="change" type="number" class="field" min="0" step="1" required /></label>
          <label class="label">Tham chiếu giao dịch<input v-model="reference" class="field" maxlength="200" /></label>
          <label class="label">Bằng chứng trả tiền thừa (nếu có)<textarea v-model="evidence" class="field" maxlength="1000" /></label>
        </fieldset>
        <p role="status">Thu ròng theo số vừa nhập: {{ net == null ? 'Chưa nhập đủ số tiền' : money(net) }}</p>
        <button class="btn-primary" :disabled="busy || loading || createUncertain || handoverUncertain">{{ payUncertain ? 'Thử lại yêu cầu thu tiền' : 'Xác nhận thu tiền thực tế' }}</button>
      </form>
      <p v-if="invoice.trangThai !== 'PAID'" class="muted">Chưa thể bàn giao pet khi hóa đơn chưa thanh toán.</p>
      <div v-if="invoice.trangThai === 'PAID' && auth.can('PET_HANDOVER')" class="space-y-4">
        <form v-for="pet in readyPets" :key="pet.maChiTietDon" :data-testid="`handover-form-${pet.maChiTietDon}`" class="space-y-3" @submit.prevent="handover(pet)">
          <label class="flex items-start gap-2"><input v-model="consents[pet.maChiTietDon]" :data-testid="`handover-consent-${pet.maChiTietDon}`" type="checkbox" :disabled="locked" required />Khách đã nhận {{ pet.tenThuCung }}</label>
          <button class="btn" :disabled="busy || payUncertain || createUncertain || (handoverUncertain && handoverTarget !== pet.maChiTietDon)">{{ handoverUncertain ? 'Thử lại bàn giao' : `Bàn giao ${pet.tenThuCung}` }}</button>
        </form>
      </div>
    </template>
  </section>
</template>
<script setup>
import { computed, nextTick, ref, watch } from "vue";
import { bookingService } from "@/services/api/bookingService";
import { useAuthStore } from "@/stores/auth";
import { useSpaCommand } from "@/composables/useSpaCommand";
import { formatCurrency as money } from "@/utils/formatters";
const props = defineProps({ booking: { type: Object, required: true } }), emit = defineEmits(["changed", "busy"]);
const auth = useAuthStore(), creation = useSpaCommand(), payment = useSpaCommand(), delivery = useSpaCommand();
const { busy: createBusy, uncertain: createUncertain } = creation, { busy: payBusy, uncertain: payUncertain } = payment, { busy: handoverBusy, uncertain: handoverUncertain } = delivery;
const busy = computed(() => createBusy.value || payBusy.value || handoverBusy.value), uncertain = computed(() => createUncertain.value || payUncertain.value || handoverUncertain.value), locked = computed(() => busy.value || uncertain.value);
const invoice = ref(props.booking.hoaDon ?? null), pets = ref(props.booking.thuCung.map(pet => ({ ...pet }))), consents = ref({});
const loading = ref(false), error = ref(""), errors = ref([]), errorSummary = ref(null), message = ref("");
const method = ref("CASH"), received = ref(""), change = ref(""), reference = ref(""), evidence = ref(""), handoverTarget = ref(null);
const canCreate = computed(() => (!invoice.value || invoice.value.trangThai === "REVOKED") && props.booking.trangThai === "IN_PROGRESS" && auth.can("INVOICE_CREATE") && pets.value.length > 0 && pets.value.some(pet => ["READY", "DONE"].includes(pet.trangThai)) && pets.value.every(pet => ["READY", "DONE", "CANCELLED"].includes(pet.trangThai)));
const readyPets = computed(() => pets.value.filter(pet => pet.trangThai === "READY"));
const net = computed(() => received.value !== "" && change.value !== "" && Number.isFinite(Number(received.value)) && Number.isFinite(Number(change.value)) ? Number(received.value) - Number(change.value) : null);
watch(() => props.booking, booking => { if (!locked.value) { pets.value = booking.thuCung.map(pet => ({ ...pet })); if (booking.hoaDon) invoice.value = booking.hoaDon; } });
watch(locked, value => emit("busy", value));
async function showError(e) { error.value = e.message || "Không thể xử lý yêu cầu."; errors.value = e.errors ?? []; await nextTick(); errorSummary.value?.focus(); }
async function failed(e) { if (e.errorCode === "AMOUNT_MISMATCH") await reload(); await showError(e); if (["FORBIDDEN", "INVALID_TRANSITION"].includes(e.errorCode)) emit("changed", { trangThai: props.booking.trangThai }); }
async function createInvoice() {
  if (!canCreate.value || busy.value || loading.value) return;
  try { const response = await creation.run({ bookingId: props.booking.maDon }, bookingService.createInvoice); invoice.value = response.data; error.value = ""; emit("changed", { invoice: response.data }); }
  catch (e) { await failed(e); }
}
async function reload() {
  if (!invoice.value || locked.value || loading.value || !auth.can("INVOICE_CREATE")) return;
  loading.value = true;
  try { const response = await bookingService.getInvoice({ invoiceId: invoice.value.maHoaDon }); if (!response.success) throw response; invoice.value = response.data; error.value = ""; }
  catch (e) { await showError(e); } finally { loading.value = false; }
}
async function pay() {
  if (!invoice.value || busy.value || loading.value) return;
  try {
    const amount = Number(received.value), returned = Number(change.value);
    if (!payUncertain.value && (received.value === "" || change.value === "" || !Number.isSafeInteger(amount) || amount <= 0 || !Number.isSafeInteger(returned) || returned < 0 || returned > amount)) throw new Error("Nhập số tiền thực nhận và đã trả lại bằng đồng nguyên, hợp lệ.");
    const response = await payment.run({ invoiceId: invoice.value.maHoaDon, phuongThuc: method.value, soTienThucNhan: amount, soTienDaTraLai: returned, thamChieu: reference.value || undefined, bangChungTraThua: evidence.value || undefined }, bookingService.checkoutInvoice);
    invoice.value = response.data; error.value = ""; message.value = "Đã ghi nhận thanh toán. Bàn giao từng bé là thao tác riêng."; emit("changed", { invoice: response.data });
  } catch (e) { await failed(e); }
}
async function handover(pet) {
  if (busy.value || loading.value || invoice.value?.trangThai !== "PAID") return;
  try {
    if (!handoverUncertain.value && !consents.value[pet.maChiTietDon]) throw new Error("Xác nhận khách đã nhận đúng thú cưng.");
    handoverTarget.value = pet.maChiTietDon;
    const response = await delivery.run({ maChiTietDon: pet.maChiTietDon, xacNhanDaNhan: consents.value[pet.maChiTietDon] === true }, bookingService.handoverPet);
    pets.value = pets.value.map(item => item.maChiTietDon === response.data.maChiTietDon ? response.data : item);
    consents.value[pet.maChiTietDon] = false; error.value = ""; message.value = "Đã bàn giao thú cưng.";
    const terminal = pets.value.every(item => ["DONE", "CANCELLED"].includes(item.trangThai));
    emit("changed", { pet: response.data, trangThai: terminal ? "COMPLETED" : "IN_PROGRESS", reloadOnly: terminal });
  } catch (e) { await failed(e); }
}
</script>

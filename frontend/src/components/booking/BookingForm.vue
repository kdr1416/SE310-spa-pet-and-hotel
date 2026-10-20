<template>
  <form
    class="grid items-start gap-6 lg:grid-cols-[1fr_340px]"
    @submit.prevent="submit"
  >
    <div class="space-y-5">
      <section class="panel">
        <h2>1. Thông tin khách hàng</h2>
        <div class="mt-5 grid gap-4 sm:grid-cols-2">
          <label class="label sm:col-span-2" for="customer-name"
            >Họ và tên *<input
              id="customer-name"
              v-model="form.customer.name"
              class="field"
              maxlength="100"
              required
              autocomplete="name" /></label
          ><label class="label" for="customer-phone"
            >Số điện thoại *<input
              id="customer-phone"
              v-model="form.customer.phone"
              class="field"
              type="tel"
              required
              autocomplete="tel" /></label
          ><label class="label" for="customer-email"
            >Email *<input
              id="customer-email"
              v-model="form.customer.email"
              class="field"
              type="email"
              required
              autocomplete="email"
          /></label>
        </div>
      </section>
      <section class="panel space-y-5">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <h2>2. Các bé đi cùng bạn</h2>
          <button
            data-testid="add-pet"
            class="btn"
            type="button"
            @click="addPet"
          >
            <Plus :size="17" aria-hidden="true" />Thêm thú cưng
          </button>
        </div>
        <fieldset
          v-for="(p, i) in form.pets"
          :key="p.key"
          data-testid="pet-card"
          class="rounded-xl border border-primary-100 bg-primary-50/50 p-4"
        >
          <legend class="px-2 font-bold">Thú cưng {{ i + 1 }}</legend>
          <div class="mb-4 text-right">
            <button
              data-testid="remove-pet"
              type="button"
              class="text-sm font-bold text-red-800 disabled:opacity-40"
              :disabled="form.pets.length === 1"
              @click="form.pets.splice(i, 1)"
            >
              Xóa bé này
            </button>
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <label class="label"
              >Tên bé *<input
                v-model="p.name"
                data-testid="pet-name"
                class="field"
                required
                maxlength="100" /></label
            ><label class="label"
              >Loài *<select v-model="p.species" class="field">
                <option value="CHO">Chó</option>
                <option value="MEO">Mèo</option>
              </select></label
            ><label class="label"
              >Giống<input v-model="p.breed" class="field" /></label
            ><label class="label"
              >Loại lông<select v-model="p.coatType" class="field">
                <option value="SHORT">Lông ngắn</option>
                <option value="LONG">Lông dài</option>
                <option value="OTHER">Khác / không lông</option>
              </select></label
            ><label class="label"
              >Cân nặng khai báo (kg) *<input
                v-model.number="p.weight"
                class="field"
                type="number"
                min=".1"
                max="150"
                step=".1"
                required
            /></label>
            <label class="label"
              >Loại dịch vụ<select v-model="p.type" class="field">
                <option value="SPA">Spa</option>
                <option value="HOTEL">Hotel</option>
                <option value="COMBO_SPA_HOTEL">Spa + Hotel</option>
              </select></label
            >
            <label v-if="p.type !== 'HOTEL'" class="label"
              >Dịch vụ / Combo *<select
                class="field"
                :value="
                  p.comboId ? 'combo:' + p.comboId : 'service:' + p.serviceId
                "
                @change="choose(p, $event.target.value)"
              >
                <option
                  v-for="s in store.catalog.services.filter(
                    (s) => s.isActive && s.loaiDichVu !== 'SPA_ADVANCED',
                  )"
                  :key="'s' + s.id"
                  :value="'service:' + s.id"
                >
                  {{ s.tenDichVu }}
                </option>
                <option
                  v-for="c in store.catalog.combos.filter((c) => c.isActive)"
                  :key="'c' + c.id"
                  :value="'combo:' + c.id"
                >
                  {{ c.tenCombo }}
                </option>
              </select></label
            >
            <template v-if="p.type !== 'SPA'"
              ><label class="label"
                >Phòng *<select v-model.number="p.roomId" class="field">
                  <option
                    v-for="r in store.catalog.rooms"
                    :key="r.id"
                    :value="r.id"
                  >
                    {{ r.tenPhong }}
                  </option>
                </select></label
              ></template>
              <label class="label sm:col-span-2"
                >Ảnh giấy tiêm chủng (tùy chọn)<input
                  class="field"
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  @change="pickVaccine(p, $event)"
                /><span class="muted"
                  >JPEG/PNG/WebP ≤ 5MB. Bạn có thể mang giấy tới quầy.
                  {{ store.catalog.vaccinePolicy?.[p.type] ? 'Dịch vụ này yêu cầu nhân viên xác minh lúc check-in.' : 'Hiện không bắt buộc xác minh vaccine.' }}</span
                ></label
            >
            <label class="label sm:col-span-2"
              >Ghi chú sức khỏe<textarea
                v-model="p.notes"
                class="field"
                rows="2"
                maxlength="1000"
              />
            </label>
          </div>
        </fieldset>
      </section>
      <section class="panel">
        <h2>3. Chọn thời gian</h2>
        <div class="mt-5 grid gap-4 sm:grid-cols-2">
          <label class="label"
            >Ngày nhận *<input
              v-model="form.date"
              class="field"
              type="date"
              :min="today"
              required /></label
          ><label class="label"
            >Ca tiếp nhận *<select v-model.number="form.shiftId" class="field">
              <option
                v-for="s in store.catalog.availability"
                :key="s.id"
                :value="s.id"
                :disabled="(spaPetCount > 0 && s.left < spaPetCount) || !!store.catalog.closedDays?.[form.date] || s.isActive === false"
              >
                {{ s.tenCa }} · {{ spaPetCount ? `${s.left} suất Spa` : 'Hẹn bàn giao Hotel · không trừ slot Spa' }}
              </option>
            </select></label
          >
          <label v-if="hasHotelPets" class="label sm:col-span-2">Ngày trả chung cho mọi bé *
            <input v-model="form.checkout" type="date" class="field" :min="checkoutMin" required />
          </label>
        </div>
        <p v-if="store.catalog.closedDays?.[form.date] || (hasHotelPets && store.catalog.closedDays?.[form.checkout])" class="mt-3 text-sm text-red-800" role="alert">Ngày đã chọn nghỉ tiếp nhận/nhận–trả. Vui lòng chọn ngày khác.</p>
        <p v-if="mixedPets" class="mt-3 text-sm text-red-800" role="alert">Đơn qua đêm cần phòng cho mọi bé. Hãy tách bé Spa đón trong ngày thành đơn riêng.</p>
        <div v-if="!hasHotelPets && store.catalog.roomAvailability?.length" class="mt-5">
          <h3>Lịch tiếp nhận Spa · 14 ngày</h3>
          <div class="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
            <button v-for="day in store.catalog.roomAvailability" :key="day.date" type="button" class="rounded-xl border border-primary-100 p-3 text-left text-sm disabled:bg-stone-100 disabled:text-stone-500" :disabled="day.closed" @click="form.date = day.date">
              <strong>{{ weekdayLabel(day.date) }}</strong><span class="mt-1 block">{{ day.closed ? 'Nghỉ tiếp nhận' : 'Chọn để xem chỗ theo ca' }}</span>
            </button>
          </div>
        </div>
        <div v-if="hasHotelPets && store.catalog.roomAvailability?.length" class="mt-5">
          <h3>Phòng còn theo từng đêm · 14 ngày từ ngày nhận</h3>
          <p class="muted mt-2">Ngày trả không tính thêm đêm. Ngày nghỉ chỉ ngừng nhận–trả, vẫn chăm sóc các bé lưu trú. Số phòng là tham khảo và được kiểm tra lại khi đặt.</p>
          <div class="table-wrap mt-3 max-h-80 overflow-auto">
            <table><thead><tr><th>Ngày</th><th v-for="r in store.catalog.rooms" :key="r.id">{{ r.tenPhong }}</th><th>Tiếp nhận</th></tr></thead>
              <tbody><tr v-for="day in store.catalog.roomAvailability" :key="day.date">
                <td><button type="button" class="font-bold text-primary-800 disabled:text-stone-400" :disabled="day.closed" @click="form.date = day.date">{{ weekdayLabel(day.date) }}</button></td>
                <td v-for="r in store.catalog.rooms" :key="r.id">{{ day.rooms.find((v) => v.id === r.id)?.left ?? 0 }} phòng</td>
                <td>{{ day.closed ? 'Nghỉ nhận–trả' : 'Nhận khách' }}</td>
              </tr></tbody></table>
          </div>
        </div>
      </section>
    </div>
    <aside class="panel space-y-5 lg:sticky lg:top-6">
      <h2>Tổng hợp lịch hẹn</h2>
      <div
        v-for="(p, i) in form.pets"
        :key="p.key"
        class="border-b border-primary-100 pb-3"
      >
        <p class="font-bold">{{ p.name || "Thú cưng " + (i + 1) }}</p>
        <p class="muted">
          {{ price.items[i]?.label || "Chọn dịch vụ và thời gian" }}
        </p>
        <p class="mt-2 font-bold">
          {{ formatCurrency(price.items[i]?.amount || 0) }}
        </p>
      </div>
      <div class="flex justify-between font-bold">
        <span>Tạm tính</span><span>{{ formatCurrency(price.total) }}</span>
      </div>
      <p class="muted">
        Giá được chốt sau khi cân thực tế. Đây là lịch hẹn mock, chưa giữ chỗ
        tại cửa hàng hoặc gửi email.
      </p>
      <ActionFeedback :busy="busy" :error="error" /><button
        class="btn-accent w-full"
        type="submit"
        :disabled="busy || !store.catalog.availability.length || mixedPets"
      >
        {{ busy ? "Đang lưu…" : "Xác nhận đặt lịch" }}
      </button>
    </aside>
  </form>
</template>
<script setup>
import { reactive, computed, onMounted, watch, toRaw } from "vue";
import { Plus } from "lucide-vue-next";
import { useWorkspaceStore } from "@/stores/workspace";
import { useAction } from "@/composables/useAction";
import {
  bookingService,
  createCustomerBooking,
} from "@/services/api/bookingService";
import { estimateBooking } from "@/utils/pricing";
import { validateBooking, validateImage } from "@/utils/bookingRules";
import { localDate, addDays, weekdayLabel } from "@/utils/dates";
import { formatCurrency } from "@/utils/formatters";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const props = defineProps({ internal: Boolean }),
  emit = defineEmits(["created"]),
  store = useWorkspaceStore(),
  { busy, error, perform } = useAction(),
  today = localDate();
const form = reactive({
  requestId: crypto.randomUUID(),
  customer: { name: "", phone: "", email: "" },
  date: today,
  checkout: addDays(today, 1),
  shiftId: 1,
  pets: [],
});
function addPet() {
  form.pets.push({
    key: crypto.randomUUID(),
    name: "",
    species: "CHO",
    breed: "",
    coatType: "SHORT",
    weight: 5,
    type: "SPA",
    serviceId:
      store.catalog.services.find(
        (s) => s.isActive && s.loaiDichVu !== "SPA_ADVANCED",
      )?.id || 1,
    comboId: null,
    roomId: store.catalog.rooms[0]?.id || 1,
    checkout: addDays(form.date, 1),
    vaccine: null,
    notes: "",
  });
}
addPet();
function choose(p, v) {
  const [type, id] = v.split(":");
  p.comboId = type === "combo" ? Number(id) : null;
  p.serviceId = type === "service" ? Number(id) : null;
}
const hasHotelPets = computed(() => form.pets.some((p) => p.type !== "SPA")),
  spaPetCount = computed(() => form.pets.filter((p) => p.type !== "HOTEL").length),
  mixedPets = computed(() => hasHotelPets.value && form.pets.some((p) => p.type === "SPA")),
  checkoutMin = computed(() => addDays(form.date || today, 1)),
  price = computed(() => {
    try {
      return estimateBooking({ ...form, pets: form.pets.map((p) => ({ ...p, checkout: form.checkout })) }, store.catalog);
    } catch {
      return { items: [], total: 0 };
    }
  });
function pickVaccine(p, e) {
  try {
    validateImage(e.target.files[0]);
    p.vaccine = e.target.files[0];
    error.value = "";
  } catch (err) {
    p.vaccine = null;
    error.value = err.message;
    e.target.value = "";
  }
}
onMounted(() => perform(() => store.loadCatalog(form.date)));
watch(
  () => form.date,
  () => {
    if (!form.date) return;
    if (form.checkout <= form.date) form.checkout = addDays(form.date, 1);
    perform(() => store.loadCatalog(form.date));
  },
);
async function submit() {
  await perform(async () => {
    const payload = {
      ...toRaw(form),
      customer: { ...toRaw(form.customer) },
      pets: form.pets.map((p) => ({ ...toRaw(p), checkout: form.checkout })),
    };
    const available =
      store.catalog.availability.find((s) => s.id === form.shiftId)?.left || 0;
    const errors = validateBooking(payload, available, store.catalog);
    if (errors.length) throw new Error(errors.join(" "));
    const result = await (props.internal
      ? bookingService.createBooking(payload)
      : createCustomerBooking(payload));
    emit("created", result.data);
  });
}
</script>

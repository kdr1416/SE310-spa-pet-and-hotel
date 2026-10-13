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
              ><label class="label"
                >Ngày trả phòng *<input
                  v-model="p.checkout"
                  class="field"
                  type="date"
                  :min="checkoutMin"
                  required /></label
              ><label class="label sm:col-span-2"
                >Ảnh giấy tiêm chủng *<input
                  class="field"
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  required
                  @change="pickVaccine(p, $event)"
                /><span class="muted"
                  >JPEG/PNG/WebP ≤ 5MB. Nhân viên sẽ kiểm tra giấy tờ khi tiếp
                  nhận.</span
                ></label
              ></template
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
                :disabled="s.left < form.pets.length"
              >
                {{ s.tenCa }} · {{ s.left }} chỗ
              </option>
            </select></label
          >
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
        :disabled="busy || !store.catalog.availability.length"
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
import { localDate, addDays } from "@/utils/dates";
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
  shiftId: 1,
  pets: [],
});
function addPet() {
  form.pets.push({
    key: crypto.randomUUID(),
    name: "",
    species: "CHO",
    breed: "",
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
const checkoutMin = computed(() => addDays(form.date || today, 1)),
  price = computed(() => {
    try {
      return estimateBooking(form, store.catalog);
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
  () => perform(() => store.loadCatalog(form.date)),
);
async function submit() {
  await perform(async () => {
    const payload = {
      ...toRaw(form),
      customer: { ...toRaw(form.customer) },
      pets: form.pets.map((p) => ({ ...toRaw(p) })),
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

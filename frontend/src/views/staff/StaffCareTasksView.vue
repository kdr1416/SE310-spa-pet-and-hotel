<template>
  <div class="space-y-6">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <p class="muted">
        Pet được phân công cho bạn. Ảnh và ghi chú là điều kiện bắt buộc trước
        khi bắt đầu / bàn giao.
      </p>
      <button class="btn" :disabled="busy" @click="load">Tải lại</button>
    </div>
    <ActionFeedback :busy="busy" :error="error" :message="message" />
    <div class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
      <article v-for="task in tasks" :key="task.d.id" class="panel space-y-4">
        <p class="break-all font-mono text-xs font-bold">
          {{ task.b.trackingCode }}
        </p>
        <h2>{{ task.d.thuCung.tenThuCung }}</h2>
        <span class="badge">{{
          getCareStatusBadge(task.d.trangThaiChamSoc).label
        }}</span>
        <p class="muted">{{ task.d.label }} · {{ task.d.canNangThucTe }}kg</p>
        <p v-if="task.d.thuCung.ghiChuDiUng" class="text-sm text-red-800">
          {{ task.d.thuCung.ghiChuDiUng }}
        </p>
        <div v-if="task.d.nhatKyChamSoc.length" class="grid grid-cols-2 gap-2">
          <div v-for="log in task.d.nhatKyChamSoc" :key="log.id">
            <div v-if="log.photo" class="h-24">
              <StoredPhoto :blob="log.photo" :alt="log.ghiChuSucKhoe" />
            </div>
            <p class="mt-1 text-xs">{{ log.ghiChuSucKhoe }}</p>
          </div>
        </div>
        <button
          v-if="task.d.trangThaiChamSoc !== 'READY'"
          class="btn-primary w-full"
          @click="open(task)"
        >
          Cập nhật tiến trình
        </button>
      </article>
    </div>
    <p v-if="!tasks.length && !busy" class="panel muted">
      Chưa có thú cưng được phân công. Nhân viên quầy cần check-in và chọn người
      phụ trách.
    </p>
    <Dialog
      v-model:visible="visible"
      modal
      header="Cập nhật chăm sóc"
      :style="{ width: '34rem', maxWidth: '95vw' }"
      ><form v-if="selected" class="space-y-4" @submit.prevent="save">
        <h3>{{ selected.d.thuCung.tenThuCung }}</h3>
        <label class="label"
          >Trạng thái tiếp theo<select v-model="form.next" class="field">
            <option v-for="s in nextStatuses" :key="s" :value="s">
              {{ getCareStatusBadge(s).label }}
            </option>
          </select></label
        ><label class="label"
          >Cân nặng ghi nhận (không sửa giá đã chốt)<input
            v-model.number="form.weight"
            class="field"
            type="number"
            min=".1"
            max="150"
            step=".1"
            required /></label
        ><label class="label"
          >Ghi chú sức khỏe<textarea
            v-model="form.note"
            class="field"
            rows="3"
            required
            maxlength="1000"
          /></label
        ><label v-if="form.next !== 'PAUSED'" class="label"
          >Ảnh tiến trình<input
            class="field"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            capture="environment"
            required
            @change="photo"
          /><span class="muted">JPEG/PNG/WebP ≤ 5MB</span></label
        ><ActionFeedback :error="error" :busy="busy" /><button
          class="btn-primary"
          :disabled="busy"
        >
          Lưu tiến trình
        </button>
      </form></Dialog
    >
  </div>
</template>
<script setup>
import { ref, reactive, computed, onMounted } from "vue";
import Dialog from "primevue/dialog";
import { useWorkspaceStore } from "@/stores/workspace";
import { useAuthStore } from "@/stores/auth";
import { bookingService } from "@/services/api/bookingService";
import { useAction } from "@/composables/useAction";
import { validateImage } from "@/utils/bookingRules";
import { getCareStatusBadge } from "@/utils/formatters";
import StoredPhoto from "@/components/shared/StoredPhoto.vue";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const store = useWorkspaceStore(),
  auth = useAuthStore(),
  visible = ref(false),
  selected = ref(null),
  form = reactive({ next: "IN_PROGRESS", photo: null, note: "", weight: 5 }),
  { busy, error, message, perform } = useAction();
const tasks = computed(() =>
    store.bookings
      .filter((b) => !["COMPLETED", "CANCELLED"].includes(b.trangThai))
      .flatMap((b) =>
        b.chiTietDonList
          .filter(
            (d) =>
              d.checkedIn && (auth.isAdmin || d.nhanVienId === auth.user.id),
          )
          .map((d) => ({ b, d })),
      ),
  ),
  nextStatuses = computed(
    () =>
      ({
        WAITING: ["IN_PROGRESS"],
        IN_PROGRESS: ["PAUSED", "READY"],
        PAUSED: ["IN_PROGRESS"],
      })[selected.value?.d.trangThaiChamSoc] || [],
  );
async function load() {
  await perform(() => store.loadBookings());
}
onMounted(load);
function open(task) {
  selected.value = task;
  form.next = {
    WAITING: "IN_PROGRESS",
    IN_PROGRESS: "READY",
    PAUSED: "IN_PROGRESS",
  }[task.d.trangThaiChamSoc];
  form.weight = task.d.canNangThucTe || 5;
  form.photo = null;
  form.note = "";
  error.value = "";
  visible.value = true;
}
function photo(e) {
  try {
    validateImage(e.target.files[0]);
    form.photo = e.target.files[0];
    error.value = "";
  } catch (err) {
    error.value = err.message;
    form.photo = null;
    e.target.value = "";
  }
}
async function save() {
  await perform(async () => {
    await bookingService.careUpdate({
      id: selected.value.b.id,
      detailId: selected.value.d.id,
      ...form,
    });
    visible.value = false;
    await store.loadBookings();
  }, "Đã cập nhật tiến trình.");
}
</script>

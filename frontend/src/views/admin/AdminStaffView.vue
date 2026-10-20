<template>
  <div class="space-y-6">
    <section class="panel">
      <h2>Tạo tài khoản nhân viên mock</h2>
      <p class="muted mt-2">
        Tài khoản mới có role STAFF. Admin có thể đổi ADMIN ↔ STAFF bên dưới.
        Nhóm quầy/spa chỉ còn là phân nhóm mô phỏng của giao diện hiện tại.
      </p>
      <form class="mt-5 grid gap-4 md:grid-cols-2" @submit.prevent="create">
        <label class="label"
          >Họ tên<input
            v-model="form.name"
            class="field"
            required
            maxlength="100" /></label
        ><label class="label"
          >Username<input
            v-model="form.username"
            class="field"
            required
            pattern="[a-zA-Z0-9_.-]{3,40}"
            autocomplete="off" /></label
        ><label class="label"
          >Nhóm công việc<select v-model="form.workGroup" class="field">
            <option value="RECEPTION">Quầy</option>
            <option value="SPA">Spa</option>
          </select></label
        ><label class="label"
          >Mật khẩu khởi tạo<input
            v-model="password"
            class="field"
            type="password"
            minlength="8"
            required
            autocomplete="new-password" /></label
        ><button class="btn-primary justify-self-start" :disabled="busy">
          Tạo tài khoản
        </button>
      </form>
    </section>
    <ActionFeedback :error="error" :busy="busy" :message="message" />
    <section class="panel">
      <div class="flex items-center justify-between gap-3">
        <h2>Danh sách nhân viên</h2>
        <button class="btn" :disabled="busy" @click="load">Tải lại</button>
      </div>
      <div class="table-wrap mt-5">
        <table>
          <thead>
            <tr>
              <th>Nhân viên</th>
              <th>Username</th>
              <th>Nhóm</th>
              <th>Trạng thái</th>
              <th>Role</th>
              <th>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="u in users" :key="u.id">
              <td>{{ u.tenNhanVien }}</td>
              <td>{{ u.tenDangNhap }}</td>
              <td>
                {{
                  u.vaiTro === "ADMIN"
                    ? "Admin"
                    : u.workGroup === "SPA"
                      ? "Spa"
                      : "Quầy"
                }}
              </td>
              <td>{{ u.isActive ? "Hoạt động" : "Đã khóa" }}</td>
              <td>
                <label class="label"><span class="sr-only">Role của {{ u.tenNhanVien }}</span>
                  <select v-model="roles[u.id]" class="field"><option value="ADMIN">ADMIN</option><option value="STAFF">STAFF</option></select>
                </label>
                <button class="btn mt-2" :disabled="busy || roles[u.id] === u.vaiTro" @click="roleTarget = u">Lưu role</button>
              </td>
              <td>
                <button
                  v-if="u.vaiTro !== 'ADMIN'"
                  class="btn"
                  :disabled="busy"
                  @click="toggle(u)"
                >
                  {{ u.isActive ? "Khóa" : "Kích hoạt" }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
    <Dialog v-model:visible="roleDialog" modal header="Xác nhận đổi quyền tài khoản" :style="{ width: '30rem', maxWidth: '95vw' }">
      <p v-if="roleTarget">Đổi {{ roleTarget.tenNhanVien }} sang <strong>{{ roles[roleTarget.id] }}</strong>? Quyền mới có hiệu lực với phiên đang mở. Luôn phải còn một Admin hoạt động.</p>
      <ActionFeedback :error="error" />
      <div class="mt-5 flex justify-end gap-2"><button class="btn" :disabled="busy" @click="roleTarget = null">Quay lại</button><button class="btn-primary" :disabled="busy" @click="changeRole">Xác nhận</button></div>
    </Dialog>
  </div>
</template>
<script setup>
import { ref, reactive, onMounted, computed } from "vue";
import Dialog from 'primevue/dialog';
import { useAuthStore } from '@/stores/auth';
import { staffService } from "@/services/api/staffService";
import { hashMockPassword } from "@/utils/mockCredentials";
import { useAction } from "@/composables/useAction";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const users = ref([]),
  auth = useAuthStore(),
  roles = reactive({}), roleTarget = ref(null),
  roleDialog = computed({ get: () => !!roleTarget.value, set: (v) => { if (!v) roleTarget.value = null; } }),
  form = reactive({ name: "", username: "", workGroup: "RECEPTION" }),
  password = ref(""),
  { busy, error, message, perform } = useAction();
async function get() {
  users.value = (await staffService.getStaff()).data;
  for (const u of users.value) roles[u.id] = u.vaiTro;
}
async function load() {
  await perform(get);
}
onMounted(load);
async function create() {
  await perform(async () => {
    const credential = await hashMockPassword(password.value);
    await staffService.createStaff({ ...form, credential });
    password.value = "";
    form.name = "";
    form.username = "";
    await get();
  }, "Đã tạo tài khoản mock.");
}
async function toggle(u) {
  await perform(async () => {
    await staffService.toggleStaff({ id: u.id });
    await get();
  }, "Đã cập nhật nhân viên.");
}
async function changeRole() {
  await perform(async () => {
    const target = roleTarget.value;
    await staffService.changeRole({ id: target.id, role: roles[target.id] });
    roleTarget.value = null;
    await auth.initAuth();
    if (auth.isAdmin) await get();
  }, 'Đã cập nhật quyền tài khoản.');
}
</script>

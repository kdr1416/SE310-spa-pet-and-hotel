<template>
  <div class="space-y-6">
    <section class="panel">
      <h2>Tạo tài khoản nhân viên mock</h2>
      <p class="muted mt-2">
        Role vẫn STAFF. Nhóm quầy/spa chỉ dùng để mô phỏng menu và quyền, chưa
        quyết định mô hình Backend.
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
  </div>
</template>
<script setup>
import { ref, reactive, onMounted } from "vue";
import { staffService } from "@/services/api/staffService";
import { hashMockPassword } from "@/utils/mockCredentials";
import { useAction } from "@/composables/useAction";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const users = ref([]),
  form = reactive({ name: "", username: "", workGroup: "RECEPTION" }),
  password = ref(""),
  { busy, error, message, perform } = useAction();
async function get() {
  users.value = (await staffService.getStaff()).data;
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
</script>

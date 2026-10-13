<template>
  <section class="panel max-w-2xl space-y-5">
    <h2>Tài khoản của bạn</h2>
    <p class="muted">
      Cập nhật hồ sơ và thông tin đăng nhập mock. Mật khẩu chỉ lưu dưới dạng dẫn
      xuất PBKDF2; không phải hệ thống xác thực production.
    </p>
    <form class="grid gap-4 sm:grid-cols-2" @submit.prevent="save">
      <label class="label"
        >Họ tên<input v-model="form.name" class="field" required /></label
      ><label class="label"
        >Username<input
          v-model="form.username"
          class="field"
          required
          pattern="[a-zA-Z0-9_.-]{3,40}"
          autocomplete="username" /></label
      ><label class="label"
        >Điện thoại<input
          v-model="form.phone"
          class="field"
          type="tel" /></label
      ><label class="label"
        >Email<input v-model="form.email" class="field" type="email" /></label
      ><label class="label"
        >Mật khẩu mới (để trống nếu giữ nguyên)<input
          v-model="password"
          class="field"
          type="password"
          minlength="8"
          autocomplete="new-password" /></label
      ><label class="label"
        >Nhập lại mật khẩu mới<input
          v-model="confirmation"
          class="field"
          type="password"
          autocomplete="new-password" /></label
      ><button class="btn-primary justify-self-start" :disabled="busy">
        Lưu thông tin
      </button>
    </form>
    <ActionFeedback :error="error" :busy="busy" :message="message" />
  </section>
</template>
<script setup>
import { ref, reactive } from "vue";
import { useAuthStore } from "@/stores/auth";
import { staffService } from "@/services/api/staffService";
import { hashMockPassword } from "@/utils/mockCredentials";
import { useAction } from "@/composables/useAction";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const auth = useAuthStore(),
  form = reactive({
    name: auth.user?.tenNhanVien || "",
    username: auth.user?.tenDangNhap || "",
    phone: auth.user?.soDienThoai || "",
    email: auth.user?.email || "",
  }),
  password = ref(""),
  confirmation = ref(""),
  { busy, error, message, perform } = useAction();
async function save() {
  await perform(async () => {
    if (password.value !== confirmation.value)
      throw new Error("Mật khẩu nhập lại không khớp.");
    const credential = password.value
      ? await hashMockPassword(password.value)
      : undefined;
    await staffService.updateAccount({ ...form, credential });
    password.value = "";
    confirmation.value = "";
    await auth.initAuth();
  }, "Đã cập nhật tài khoản.");
}
</script>

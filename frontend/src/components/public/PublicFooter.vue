<template>
  <footer class="no-print border-t border-primary-100 bg-white">
    <div class="page grid gap-8 md:grid-cols-3">
      <div>
        <h3>PetSpa & Hotel</h3>
        <p class="muted mt-3">
          Chăm sóc dịu dàng, theo dõi minh bạch cho từng người bạn nhỏ.
        </p>
      </div>
      <div>
        <h3>Ghé thăm chúng mình</h3>
        <p class="muted mt-3">
          Thông tin liên hệ chính thức đang được nhóm cập nhật.
        </p>
        <RouterLink
          to="/about"
          class="mt-3 inline-block font-bold text-primary-700"
          >Thông tin liên hệ →</RouterLink
        >
      </div>
      <div class="space-y-3">
        <RouterLink class="block font-bold" to="/tracking"
          >Tra cứu lịch hẹn</RouterLink
        ><RouterLink class="block font-bold" to="/login"
          >Đăng nhập nội bộ</RouterLink
        >
        <p class="muted">© 2026 PetSpa & Hotel · Bản demo đồ án</p>
      </div>
    </div>
  </footer>
</template>

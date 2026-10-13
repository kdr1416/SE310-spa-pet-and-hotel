<template>
  <div class="flex min-h-screen flex-col">
    <PublicNavbar />
    <main id="main-content" class="flex-1"><RouterView /></main>
    <PublicFooter />
  </div>
</template>
<script setup>
import PublicNavbar from "@/components/public/PublicNavbar.vue";
import PublicFooter from "@/components/public/PublicFooter.vue";
</script>

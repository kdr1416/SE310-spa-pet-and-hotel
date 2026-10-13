<template>
  <img
    v-if="url"
    :src="url"
    :alt="alt"
    class="h-full w-full rounded-xl object-cover"
    @error="url = ''"
  />
  <div
    v-else
    class="grid h-full min-h-24 place-items-center rounded-xl bg-primary-50 text-primary-700"
  >
    <ImageOff aria-hidden="true" /><span class="text-xs">{{ alt }}</span>
  </div>
</template>
<script setup>
import { ref, watch, onBeforeUnmount } from "vue";
import { ImageOff } from "lucide-vue-next";
const props = defineProps({
  blob: [Blob, String],
  alt: { type: String, default: "Ảnh chăm sóc" },
});
const url = ref("");
let objectUrl;
function release() {
  if (objectUrl) URL.revokeObjectURL(objectUrl);
  objectUrl = null;
}
watch(
  () => props.blob,
  (b) => {
    release();
    url.value =
      typeof b === "string"
        ? b
        : b instanceof Blob
          ? (objectUrl = URL.createObjectURL(b))
          : "";
  },
  { immediate: true },
);
onBeforeUnmount(release);
</script>

import { ref } from "vue";
export function useAction() {
  const busy = ref(false),
    error = ref(""),
    message = ref("");
  async function perform(fn, success = "") {
    if (busy.value) return;
    busy.value = true;
    error.value = "";
    message.value = "";
    try {
      const result = await fn();
      message.value = success;
      return result;
    } catch (e) {
      error.value = e.message || "Không thể thực hiện thao tác.";
    } finally {
      busy.value = false;
    }
  }
  return { busy, error, message, perform };
}

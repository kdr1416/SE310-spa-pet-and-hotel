import { ref } from "vue";

// A pending logical command owns its requestId, fields and File until its outcome is known.
export function useSpaCommand() {
  const busy = ref(false), uncertain = ref(false);
  let pending = null;
  async function run(fields, send) {
    if (busy.value) return;
    if (!pending) pending = { fields: { ...fields, requestId: crypto.randomUUID() }, send };
    busy.value = true;
    try {
      const response = await pending.send(pending.fields);
      if (response.success === false) throw response;
      pending = null; uncertain.value = false;
      return response;
    } catch (error) {
      uncertain.value = !error.errorCode || error.errorCode === "TEMPORARY_FAILURE";
      if (!uncertain.value) pending = null;
      throw error;
    } finally { busy.value = false; }
  }
  return { busy, uncertain, run };
}

import { ref } from "vue";

// A pending logical command owns its requestId, fields and File until its outcome is known.
export function useSpaCommand() {
  const busy = ref(false), uncertain = ref(false);
  let pending = null;
  const fileIds = new WeakMap();
  let nextFileId = 0;
  function signature(fields) {
    return JSON.stringify(fields, (_key, value) => {
      if (typeof File !== "undefined" && value instanceof File) {
        if (!fileIds.has(value)) fileIds.set(value, ++nextFileId);
        return { fileId: fileIds.get(value) };
      }
      return value;
    });
  }
  async function run(fields, send) {
    if (busy.value) return;
    const identity = signature(fields);
    if (!pending || (!uncertain.value && identity !== pending.identity))
      pending = { identity, fields: { ...fields, requestId: crypto.randomUUID() }, send };
    busy.value = true;
    try {
      const response = await pending.send(pending.fields);
      if (response.success === false) throw response;
      pending = null; uncertain.value = false;
      return response;
    } catch (error) {
      uncertain.value = !error.errorCode || error.errorCode === "TEMPORARY_FAILURE";
      throw error;
    } finally { busy.value = false; }
  }
  return { busy, uncertain, run };
}

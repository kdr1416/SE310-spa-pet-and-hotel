import { ref } from "vue";

// A pending logical command owns its requestId, fields and File until its outcome is known.
export function useSpaCommand() {
  const busy = ref(false), uncertain = ref(false);
  let pending = null;
  let generation = 0;
  function reset() { generation++; pending = null; busy.value = false; uncertain.value = false; }
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
    const current = generation, command = pending;
    try {
      const response = await command.send(command.fields);
      if (current !== generation) throw { errorCode: "COMMAND_SCOPE_CHANGED", message: "Phiên thao tác đã thay đổi. Vui lòng tải lại." };
      if (response.success === false) throw response;
      pending = null; uncertain.value = false;
      return response;
    } catch (error) {
      if (current !== generation) throw { errorCode: "COMMAND_SCOPE_CHANGED", message: "Phiên thao tác đã thay đổi. Vui lòng tải lại." };
      uncertain.value = !error.errorCode || error.errorCode === "TEMPORARY_FAILURE";
      throw error;
    } finally { if (current === generation) busy.value = false; }
  }
  return { busy, uncertain, run, reset };
}

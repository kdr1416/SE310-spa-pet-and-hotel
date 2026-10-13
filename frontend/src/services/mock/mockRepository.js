import { createSeed } from "./seed";
import { execute } from "./mockOperations";
let database;
function openDatabase() {
  if (database) return database;
  database = new Promise((resolve, reject) => {
    if (!globalThis.indexedDB) {
      reject(new Error("Trình duyệt không hỗ trợ IndexedDB."));
      return;
    }
    const request = indexedDB.open("petspa-frontend-mock", 1);
    request.onupgradeneeded = () => request.result.createObjectStore("data");
    request.onerror = () => {
      database = null;
      reject(new Error("Không mở được dữ liệu mock. Kiểm tra quyền lưu trữ."));
    };
    request.onblocked = () => {
      database = null;
      reject(new Error("Đóng tab PetSpa cũ rồi thử lại."));
    };
    request.onsuccess = () => {
      const db = request.result;
      db.onversionchange = () => {
        db.close();
        database = null;
      };
      resolve(db);
    };
  });
  return database;
}
export async function transaction(operation, { commitError = false } = {}) {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("data", "readwrite");
    let result, error;
    const request = tx.objectStore("data").get("state");
    request.onsuccess = () => {
      const state = request.result || createSeed();
      try {
        if (state.version !== 1)
          throw new Error(
            "Phiên bản dữ liệu không hỗ trợ; dữ liệu được giữ nguyên.",
          );
        result = operation(state);
      } catch (e) {
        error = e;
        if (!commitError) {
          tx.abort();
          return;
        }
      }
      tx.objectStore("data").put(state, "state");
    };
    tx.oncomplete = () => (error ? reject(error) : resolve(result));
    tx.onabort = () =>
      reject(error || new Error("Không lưu được dữ liệu; thao tác đã hủy."));
    tx.onerror = () => {};
  });
}
export function mockCall(action, payload, actorId = null) {
  return transaction((s) => execute(s, action, payload, actorId), {
    commitError: action === "lookupBooking",
  });
}

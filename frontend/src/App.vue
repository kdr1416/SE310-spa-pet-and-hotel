<template>
  <a
    href="#main-content"
    class="sr-only z-50 rounded-lg bg-white p-3 focus:not-sr-only focus:fixed focus:left-3 focus:top-3"
    >Đến nội dung chính</a
  ><RouterView />
</template>
<script setup>
import { onMounted, onBeforeUnmount } from "vue";
import { useRouter } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { useWorkspaceStore } from "@/stores/workspace";
import { IS_MOCK_ENABLED } from "@/services/api/httpClient";
const auth = useAuthStore(), workspace = useWorkspaceStore(), router = useRouter();
let sessionTimer;
function clearLocalSession() {
  workspace.resetPrivate();
  // Do not call logout here: another tab may already have written its new shared session.
  auth.user = null; auth.token = null;
}
function sessionChanged(event) {
  if (IS_MOCK_ENABLED || ![null, "petspa_auth_token", "petspa_auth_user"].includes(event.key)) return;
  clearLocalSession(); clearTimeout(sessionTimer);
  sessionTimer = setTimeout(async () => {
    await auth.initAuth();
    const home = !auth.isAuthenticated ? "/login" : auth.isAdmin ? "/admin/overview"
      : auth.can("BOOKING_READ") ? "/staff/overview" : auth.can("CARE_UPDATE") ? "/staff/care-tasks" : "/staff/settings";
    await router.replace(home);
    window.dispatchEvent(new Event("petspa-workspace-updated"));
  }, 0);
}
function unauthorized() { clearTimeout(sessionTimer); clearLocalSession(); router.replace("/login"); }
onMounted(() => { window.addEventListener("storage", sessionChanged); window.addEventListener("auth:unauthorized", unauthorized); });
onBeforeUnmount(() => { clearTimeout(sessionTimer); window.removeEventListener("storage", sessionChanged); window.removeEventListener("auth:unauthorized", unauthorized); });
</script>

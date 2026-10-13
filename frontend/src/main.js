import { createApp } from "vue";
import { createPinia } from "pinia";
import PrimeVue from "primevue/config";
import Aura from "@primevue/themes/aura";
import { definePreset } from "@primevue/themes";
import ToastService from "primevue/toastservice";
import ConfirmationService from "primevue/confirmationservice";

import router from "./router";
import App from "./App.vue";
import "./assets/main.css";
import "primeicons/primeicons.css";

const app = createApp(App);
const pinia = createPinia();

app.use(pinia);
app.use(router);
app.use(PrimeVue, {
  theme: {
    preset: definePreset(Aura, {
      semantic: {
        primary: {
          50: "#fff7ed",
          100: "#ffedd5",
          200: "#fed7aa",
          300: "#fdba74",
          400: "#fb923c",
          500: "#ea580c",
          600: "#c2410c",
          700: "#9a3412",
          800: "#7c2d12",
          900: "#431407",
          950: "#431407",
        },
        colorScheme: {
          light: {
            primary: {
              color: "{primary.600}",
              inverseColor: "#ffffff",
              hoverColor: "{primary.700}",
              activeColor: "{primary.800}",
            },
          },
        },
      },
    }),
    options: {
      darkModeSelector: ".dark",
      cssLayer: false,
    },
  },
});
app.use(ToastService);
app.use(ConfirmationService);

app.mount("#app");

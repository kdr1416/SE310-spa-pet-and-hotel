// @vitest-environment jsdom
import { mount } from "@vue/test-utils";
import { expect, it } from "vitest";
import ServiceCard from "../../src/components/public/ServiceCard.vue";
it.each([null, undefined, NaN])("shows unavailable price for %s instead of a fabricated zero", price => {
  const wrapper = mount(ServiceCard, { props: { item: { title: "Tắm", category: "Spa", price } } });
  expect(wrapper.text()).toContain("Chưa có giá");
  expect(wrapper.text()).not.toContain("0 ₫");
});

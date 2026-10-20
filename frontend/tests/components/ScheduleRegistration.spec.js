// @vitest-environment jsdom
import { it, expect, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { addDays, localDate, weekDays } from '../../src/utils/dates';
import ScheduleWorkspace from '../../src/components/schedule/ScheduleWorkspace.vue';

const mocked = vi.hoisted(() => ({ getSchedule: vi.fn(), registerShift: vi.fn(async () => ({})) }));
vi.mock('../../src/services/api/scheduleService', () => ({ scheduleService: mocked }));

it('selects a Hotel-care shift from a closed-day calendar and sends it only after confirmation', async () => {
  const week = addDays(weekDays(localDate())[0], 7);
  mocked.getSchedule.mockResolvedValue({ data: {
    periods: [{ id: 1, week, open: true, published: false, openAt: new Date(Date.now()-1000).toISOString(), deadline: new Date(Date.now()+86400000).toISOString() }],
    registrations: [], assignments: [], users: [],
    shifts: [{ id: 1, tenCa: 'Sáng', isActive: true }],
    closedDays: { [week]: { reason: 'Nghỉ nhận khách' } },
  } });
  const w = mount(ScheduleWorkspace, { global: { stubs: { Dialog: { props: ['visible'], template: '<div v-if="visible"><slot /></div>' } } } });
  await flushPromises();
  await w.get('input[type=date]').setValue(week);
  const button = w.findAll('button').find((b) => b.text().includes('Chọn Sáng · trực Hotel'));
  expect(button).toBeTruthy();
  await button.trigger('click');
  expect(w.text()).toContain('Thứ Hai');
  expect(mocked.registerShift).not.toHaveBeenCalled();
  await w.findAll('button').find((b) => b.text() === 'Gửi đăng ký').trigger('click');
  await flushPromises();
  expect(mocked.registerShift).toHaveBeenCalledWith({ periodId: 1, date: week, shiftId: 1, purpose: 'HOTEL_CARE' });
  w.unmount();
});

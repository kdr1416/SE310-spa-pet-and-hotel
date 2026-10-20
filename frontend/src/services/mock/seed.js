import {
  mockDichVuList,
  mockComboList,
  mockLoaiPhong,
  mockCaLamViec,
  mockUsers,
} from "../api/mockData";
import { localDate, weekDays, addDays } from "../../utils/dates";
import { upgradeMockState } from "./bookingAvailability";
import { execute } from "./mockOperations";
export function createSeed({ demo = false } = {}) {
  const today = localDate(),
    nextWeek = addDays(weekDays(today)[0], 7);
  const state = upgradeMockState({
    version: 1,
    catalog: structuredClone({
      services: mockDichVuList,
      combos: mockComboList,
      rooms: mockLoaiPhong,
      shifts: mockCaLamViec,
    }),
    users: structuredClone(mockUsers).map((u) => ({
      ...u,
      workGroup: u.id === 3 ? "SPA" : "RECEPTION",
    })),
    bookings: [],
    requests: [],
    capacity: {},
    ledger: [],
    attempts: {},
    registrations: [],
    periods: [
      {
        id: 1,
        week: nextWeek,
        openAt: new Date(Date.now() - 86400000).toISOString(),
        deadline: new Date(Date.now() + 6 * 86400000).toISOString(),
        capacity: 3,
        open: true,
        published: false,
      },
    ],
    surchargeRules: [
      { id: 1, name: "Gỡ rối lông", amount: 50000 },
      { id: 2, name: "Đón trễ", amount: 30000 },
    ],
  });
  if (demo) {
    const holiday = addDays(nextWeek, 9);
    state.closedDays[holiday] = { reason: 'Nghỉ tiếp nhận · vẫn chăm sóc Hotel' };
    state.capacity[`${nextWeek}:1`] = 0;
    for (const [name, type, date, checkout] of [
      ['Milo', 'SPA', addDays(nextWeek, 1), null],
      ['Luna', 'HOTEL', addDays(holiday, -1), addDays(holiday, 2)],
    ]) {
      execute(state, 'createBooking', {
        requestId: `seed-${name}`, date, shiftId: 1,
        customer: { name: 'Khách demo', phone: '0900000000', email: 'demo@example.test' },
        pets: [{ name, species: 'CHO', weight: 5, type, serviceId: state.catalog.services.find((s) => s.isActive && s.loaiDichVu !== 'SPA_ADVANCED')?.id,
          roomId: state.catalog.rooms[0]?.id, checkout, coatType: type === 'SPA' ? 'LONG' : 'SHORT' }],
      }, null);
    }
  }
  return state;
}

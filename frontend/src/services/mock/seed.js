import {
  mockDichVuList,
  mockComboList,
  mockLoaiPhong,
  mockCaLamViec,
  mockUsers,
} from "../api/mockData";
import { localDate, weekDays, addDays } from "../../utils/dates";
export function createSeed() {
  const today = localDate(),
    nextWeek = addDays(weekDays(today)[0], 7);
  return {
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
  };
}

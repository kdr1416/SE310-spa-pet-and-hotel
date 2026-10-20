import { requireAccess } from "../../utils/permissions";
import {
  normalizePhone,
  validateBooking,
  validateImage,
  deriveBookingStatus,
} from "../../utils/bookingRules";
import { estimateBooking, estimatePet } from "../../utils/pricing";
import { addDays, localDate, weekDays, dateNumber } from "../../utils/dates";
import { upgradeMockState, hasSpa, hasHotel, assertBookingAvailability, roomCalendar } from "./bookingAvailability";

const find = (items, id, label = "Dữ liệu") => {
  const item = items.find((x) => String(x.id) === String(id));
  if (!item) throw new Error(`${label} không tồn tại.`);
  return item;
};
const id = () => crypto.randomUUID();
const cleanUser = ({ credential, ...user }) => user;
const assert = (valid, message) => {
  if (!valid) throw new Error(message);
};
export function remaining(state, date, shiftId) {
  const shift = find(state.catalog.shifts, shiftId, "Ca");
  const total = state.capacity[`${date}:${shiftId}`] ?? shift.slotPetMacDinh;
  const reserved = state.bookings
    .filter(
      (b) =>
        b.date === date &&
        b.shiftId === Number(shiftId) &&
        b.trangThai !== "CANCELLED",
    )
    .reduce(
      (n, b) => n + b.chiTietDonList.filter((d) => !d.laDichVuThem && hasSpa(d.input)).length,
      0,
    );
  return { total, reserved, left: total - reserved };
}
function ownBooking(state, p, now) {
  const phone = normalizePhone(p.phone),
    code = String(p.trackingCode || "")
      .trim()
      .toUpperCase();
  const attempt = state.attempts[phone];
  if (attempt?.until > now)
    throw new Error("Tra cứu tạm khóa trong 15 phút. Vui lòng thử lại sau.");
  const b = state.bookings.find(
    (b) => b.trackingCode === code && b.khachHang.soDienThoai === phone,
  );
  if (!b || !phone || !code) {
    const inWindow =
      Number.isFinite(attempt?.startedAt) && now - attempt.startedAt < 900000;
    const count = inWindow ? attempt.count + 1 : 1;
    state.attempts[phone] = {
      count,
      startedAt: inWindow ? attempt.startedAt : now,
      until: count > 5 ? now + 900000 : 0,
    };
    throw new Error("Thông tin tra cứu không chính xác.");
  }
  delete state.attempts[phone];
  return b;
}
function mutable(b) {
  assert(
    !["COMPLETED", "CANCELLED"].includes(b.trangThai) &&
      b.hoaDon?.trangThaiThanhToan !== "PAID",
    "Đơn đã đóng, không thể chỉnh sửa.",
  );
  assert(!b.hoaDon, "Đã tạo hóa đơn snapshot; không thể thay đổi dịch vụ/giá.");
}
function assignmentValid(s, registration, employeeId) {
  assert(!s.closedDays[registration.date] || registration.purpose === "HOTEL_CARE", "Ngày nghỉ chỉ đăng ký ca chăm sóc Hotel.");
  const u = find(s.users, employeeId, "Nhân viên");
  assert(u.isActive, "Nhân viên không hoạt động.");
  const period = find(s.periods, registration.periodId);
  assert(!period.published, "Lịch đã chốt.");
  const shift = find(s.catalog.shifts, registration.shiftId);
  const approved = s.registrations.filter(
    (r) =>
      r.id !== registration.id &&
      r.status === "APPROVED" &&
      r.date === registration.date,
  );
  assert(
    approved.filter((r) => r.shiftId === registration.shiftId).length <
      period.capacity,
    "Ca đã đủ nhân viên.",
  );
  assert(
    !approved.some(
      (r) =>
        r.employeeId === employeeId &&
        (() => {
          const other = find(s.catalog.shifts, r.shiftId);
          return (
            shift.gioBatDau < other.gioKetThuc &&
            other.gioBatDau < shift.gioKetThuc
          );
        })(),
    ),
    "Nhân viên có ca trùng giờ.",
  );
}
function detailFrom(p, price) {
  return {
    id: id(),
    thuCung: {
      tenThuCung: p.name,
      loaiPet: p.species,
      giongLoai: p.breed || "",
      canNangDangKy: Number(p.weight),
      ghiChuDiUng: p.notes || "",
      loaiLong: p.coatType || null,
    },
    input: structuredClone(p),
    label: price.label,
    donGiaChot: price.amount,
    checkedIn: false,
    trangThaiChamSoc: "WAITING",
    nhanVienId: null,
    nhatKyChamSoc: [],
    laDichVuThem: false,
  };
}
/** Synchronous transaction body. Caller commits only successful operations except lookup attempt counters. */
export function execute(s, action, p = {}, actorId = null, now = Date.now()) {
  upgradeMockState(s, now);
  const actor = s.users.find((u) => u.id === Number(actorId));
  const allow = (c) => requireAccess(actor, c);
  const timestamp = new Date(now).toISOString();
  switch (action) {
    case "catalog":
      return {
        ...s.catalog,
        surchargeRules: s.surchargeRules,
        vaccinePolicy: { ...s.settings.vaccinePolicy },
        closedDays: s.closedDays,
        roomAvailability: p.date && Number.isFinite(dateNumber(p.date)) ? roomCalendar(s, p.date, p.days) : [],
        availability: p.date
          ? s.catalog.shifts.map((shift) => ({
              ...shift,
              ...remaining(s, p.date, shift.id),
            }))
          : [],
      };
    case "demoUser": {
      const u = find(s.users, p.id, "Tài khoản");
      assert(u.isActive && u.id <= 3, "Tài khoản demo không hoạt động.");
      return cleanUser(u);
    }
    case "currentUser":
      assert(actor?.isActive, "Phiên đăng nhập không còn hợp lệ.");
      return cleanUser(actor);
    case "getWorkspace": {
      allow("BOOKING_READ");
      return {
        bookings: s.bookings,
        users: s.users.filter((u) => u.isActive).map(cleanUser),
        requests: s.requests.filter((r) => r.type === "ADDON"),
      };
    }
    case "createBooking": {
      if (actorId !== null) allow("BOOKING_MANAGE");
      assert(
        typeof p.requestId === "string" && p.requestId.length > 5,
        "Thiếu mã thao tác.",
      );
      const previous = s.bookings.find((b) => b.requestId === p.requestId);
      if (previous) return previous;
      const shift = find(s.catalog.shifts, p.shiftId, "Ca");
      assert(shift.isActive, "Ca không hoạt động.");
      const errors = validateBooking(
        p,
        remaining(s, p.date, p.shiftId).left,
        s.catalog,
      );
      assert(!errors.length, errors.join(" "));
      assertBookingAvailability(s, p);
      const prices = estimateBooking(p, s.catalog);
      let code;
      do {
        code =
          "TRK-" +
          Array.from(crypto.getRandomValues(new Uint8Array(8)), (n) =>
            n.toString(16).padStart(2, "0"),
          )
            .join("")
            .toUpperCase();
      } while (s.bookings.some((b) => b.trackingCode === code));
      const b = {
        id: id(),
        requestId: p.requestId,
        trackingCode: code,
        khachHang: {
          tenKhachHang: p.customer.name.trim(),
          soDienThoai: normalizePhone(p.customer.phone),
          email: p.customer.email.trim(),
        },
        date: p.date,
        endDate: p.pets.some(hasHotel) ? p.pets[0].checkout : p.date,
        bookingEmail: p.customer.email.trim().toLowerCase(),
        shiftId: Number(p.shiftId),
        trangThai: "PENDING",
        thoiGianBatDau: `${p.date}T${shift.gioBatDau}:00+07:00`,
        tongTienTamTinh: prices.total,
        chiTietDonList: p.pets.map((pet, i) =>
          detailFrom(pet, prices.items[i]),
        ),
        createdAt: timestamp,
        updatedAt: timestamp,
        hoaDon: null,
      };
      s.bookings.push(b);
      return b;
    }
    case "lookupBooking": {
      const b = ownBooking(s, p, now);
      return { ...b, requests: s.requests.filter((r) => r.bookingId === b.id) };
    }
    case "reviewBooking": {
      allow("BOOKING_MANAGE");
      const b = find(s.bookings, p.id, "Đơn");
      if (b.trangThai === p.decision && p.decision === "CANCELLED" && b.lyDoHuy === p.reason?.trim()) return b;
      assert(b.trangThai === "PENDING", "Đơn đã được xử lý.");
      assert(
        ["CONFIRMED", "CANCELLED"].includes(p.decision),
        "Quyết định không hợp lệ.",
      );
      if (p.decision === "CANCELLED") {
        assert(typeof p.reason === "string" && p.reason.trim().length > 0 && p.reason.trim().length <= 1000, "Nhập lý do từ chối (tối đa 1000 ký tự).");
        b.loaiHuy = "STAFF_REJECTED";
        b.lyDoHuy = p.reason.trim();
        b.huyAt = timestamp;
        b.nguoiHuyId = actor.id;
      }
      b.trangThai = p.decision;
      b.updatedAt = timestamp;
      return b;
    }
    case "updateBooking": {
      allow("BOOKING_MANAGE");
      const b = find(s.bookings, p.id, "Đơn");
      mutable(b);
      b.ghiChuKhach = String(p.note || "").slice(0, 1000);
      b.updatedAt = timestamp;
      return b;
    }
    case "checkIn": {
      allow("BOOKING_MANAGE");
      const b = find(s.bookings, p.id, "Đơn");
      mutable(b);
      assert(b.trangThai !== "PENDING", "Chấp nhận đơn trước khi tiếp nhận.");
      const d = find(b.chiTietDonList, p.detailId, "Thú cưng");
      assert(!d.checkedIn, "Thú cưng đã được tiếp nhận.");
      const policyRequired = s.settings.vaccinePolicy[d.input.type];
      assert(!policyRequired || p.vaccineVerified === true, "Nhân viên cần xác minh giấy tiêm chủng trước khi tiếp nhận.");
      const u = find(s.users, p.employeeId, "Nhân viên");
      assert(
        u.isActive && u.vaiTro === "STAFF" && u.workGroup === "SPA",
        "Chọn nhân viên spa đang hoạt động.",
      );
      assert(p.coat?.trim(), "Nhập tình trạng lông.");
      assert(
        Number.isFinite(Number(p.weight)) && p.weight >= 0.1 && p.weight <= 150,
        "Cân nặng phải từ 0.1 đến 150kg.",
      );
      const price = estimatePet(
        { ...d.input, weight: Number(p.weight) },
        b.date,
        s.catalog,
      );
      d.canNangThucTe = Number(p.weight);
      if (p.vaccineVerified === true) d.vaccineVerification = { by: actor.id, at: timestamp };
      d.tinhTrangLong = p.coat;
      d.donGiaChot = price.amount;
      d.checkedIn = true;
      d.nhanVienId = u.id;
      b.tongTienTamTinh = b.chiTietDonList.reduce(
        (n, d) => n + d.donGiaChot,
        0,
      );
      b.trangThai = deriveBookingStatus(b.chiTietDonList, b.trangThai);
      b.updatedAt = timestamp;
      return b;
    }
    case "careUpdate": {
      allow("CARE_UPDATE");
      const b = find(s.bookings, p.id, "Đơn");
      mutable(b);
      const d = find(b.chiTietDonList, p.detailId, "Thú cưng");
      assert(d.checkedIn, "Thú cưng chưa check-in.");
      assert(
        actor.vaiTro === "ADMIN" || d.nhanVienId === actor.id,
        "Thú cưng không được phân công cho bạn.",
      );
      const transitions = {
        WAITING: ["IN_PROGRESS"],
        IN_PROGRESS: ["PAUSED", "READY"],
        PAUSED: ["IN_PROGRESS"],
        READY: [],
      };
      assert(
        transitions[d.trangThaiChamSoc]?.includes(p.next),
        "Chuyển trạng thái không hợp lệ.",
      );
      assert(p.note?.trim(), "Nhập ghi chú sức khỏe.");
      if (p.next !== "PAUSED") validateImage(p.photo);
      if (p.weight !== undefined) {
        const w = Number(p.weight);
        assert(
          Number.isFinite(w) && w >= 0.1 && w <= 150,
          "Cân nặng không hợp lệ.",
        );
        d.canNangThucTe = w;
      }
      d.nhatKyChamSoc.push({
        id: id(),
        nhanVienId: actor.id,
        loaiNhatKy:
          p.next === "READY"
            ? "READY_CHECK_OUT"
            : p.next === "PAUSED"
              ? "SU_CO"
              : "IN_PROGRESS",
        photo: p.photo || null,
        ghiChuSucKhoe: p.note.trim(),
        thoiGianGhiNhan: timestamp,
      });
      d.trangThaiChamSoc = p.next;
      b.trangThai = deriveBookingStatus(b.chiTietDonList, b.trangThai);
      b.updatedAt = timestamp;
      return b;
    }
    case "createInvoice": {
      allow("BOOKING_MANAGE");
      const b = find(s.bookings, p.id, "Đơn");
      assert(
        b.trangThai === "READY",
        "Tất cả thú cưng phải sẵn sàng bàn giao.",
      );
      if (b.hoaDon) return b.hoaDon;
      b.hoaDon = {
        id: id(),
        tongTienDichVu: b.chiTietDonList.reduce((n, d) => n + d.donGiaChot, 0),
        tongPhuPhi: (b.phuPhi || []).reduce((n, f) => n + f.amount, 0),
        ctHoaDonList: b.chiTietDonList.map((d) => ({
          id: id(),
          tenKhoanMuc: d.label,
          soLuong: 1,
          donGiaSnapshot: d.donGiaChot,
          thanhTien: d.donGiaChot,
        })),
        phuPhiList: structuredClone(b.phuPhi || []),
        daThanhToan: 0,
        trangThaiThanhToan: "UNPAID",
        createdAt: timestamp,
      };
      b.hoaDon.tongThanhToan = b.hoaDon.tongTienDichVu + b.hoaDon.tongPhuPhi;
      return b.hoaDon;
    }
    case "addSurcharge": {
      allow("BOOKING_MANAGE");
      const b = find(s.bookings, p.id, "Đơn");
      mutable(b);
      const rule = find(s.surchargeRules, p.ruleId, "Phụ phí");
      assert(p.note?.trim(), "Nhập lý do phụ phí.");
      b.phuPhi ||= [];
      b.phuPhi.push({
        id: id(),
        name: rule.name,
        amount: rule.amount,
        note: p.note,
      });
      return b;
    }
    case "confirmPayment": {
      allow("BOOKING_MANAGE");
      const b = find(s.bookings, p.id, "Đơn");
      assert(b.hoaDon, "Chưa có hóa đơn.");
      assert(
        b.trangThai === "READY" && b.hoaDon.trangThaiThanhToan === "UNPAID",
        "Đơn đã thanh toán hoặc chưa sẵn sàng.",
      );
      assert(
        ["CASH", "BANK_TRANSFER"].includes(p.method),
        "Phương thức không hợp lệ.",
      );
      b.hoaDon.trangThaiThanhToan = "PAID";
      b.hoaDon.phuongThucThanhToan = p.method;
      b.hoaDon.daThanhToan = b.hoaDon.tongThanhToan;
      b.trangThai = "COMPLETED";
      b.updatedAt = timestamp;
      b.chiTietDonList.forEach((d) => (d.trangThaiChamSoc = "DONE"));
      s.ledger.push({
        id: id(),
        bookingId: b.id,
        type: "PAYMENT",
        amount: b.hoaDon.daThanhToan,
        date: localDate(new Date(now)),
        createdAt: timestamp,
      });
      return b;
    }
    case "customerRequest": {
      const b = ownBooking(s, p, now);
      assert(
        p.note?.trim() && p.note.length <= 2000,
        "Nhập nội dung (tối đa 2000 ký tự).",
      );
      assert(
        ["ADDON", "REVIEW", "COMPLAINT", "REFUND"].includes(p.type),
        "Yêu cầu không hợp lệ.",
      );
      if (p.type === "ADDON") {
        mutable(b);
        assert(
          ["CONFIRMED", "IN_PROGRESS"].includes(b.trangThai),
          "Chỉ thêm dịch vụ sau khi đơn được chấp nhận và trước khi sẵn sàng.",
        );
        find(b.chiTietDonList, p.detailId, "Thú cưng");
        const service = find(s.catalog.services, p.serviceId, "Dịch vụ");
        assert(
          service.isActive && service.loaiDichVu !== "SPA_ADVANCED",
          "Chọn dịch vụ đơn đang hoạt động.",
        );
      } else
        assert(
          b.trangThai === "COMPLETED",
          "Chỉ gửi phản hồi khi đơn đã hoàn tất.",
        );
      if (p.type === "REVIEW") {
        assert(
          Number.isInteger(Number(p.rating)) && p.rating >= 1 && p.rating <= 5,
          "Chọn 1–5 sao.",
        );
        assert(
          !s.requests.some((r) => r.type === "REVIEW" && r.bookingId === b.id),
          "Mỗi đơn chỉ được đánh giá một lần.",
        );
      }
      const r = {
        id: id(),
        bookingId: b.id,
        trackingCode: b.trackingCode,
        type: p.type,
        note: p.note.trim(),
        rating: p.type === "REVIEW" ? Number(p.rating) : null,
        detailId: p.detailId,
        serviceId: Number(p.serviceId),
        status: "PENDING",
        createdAt: timestamp,
      };
      s.requests.push(r);
      return r;
    }
    case "reviewRequest": {
      const r = find(s.requests, p.id, "Yêu cầu");
      allow(r.type === "ADDON" ? "BOOKING_MANAGE" : "FINANCE_MANAGE");
      assert(
        r.status === "PENDING" && r.type !== "REVIEW",
        "Yêu cầu đã xử lý hoặc là đánh giá.",
      );
      assert(
        ["APPROVED", "REJECTED"].includes(p.decision),
        "Quyết định không hợp lệ.",
      );
      const b = find(s.bookings, r.bookingId);
      if (p.decision === "APPROVED" && r.type === "ADDON") {
        mutable(b);
        assert(
          ["CONFIRMED", "IN_PROGRESS"].includes(b.trangThai),
          "Đơn không còn nhận thêm dịch vụ.",
        );
        const d = find(b.chiTietDonList, r.detailId);
        const input = {
          ...d.input,
          type: "SPA",
          comboId: null,
          serviceId: r.serviceId,
          weight: d.canNangThucTe || d.input.weight,
        };
        const extra = detailFrom(input, estimatePet(input, b.date, s.catalog));
        extra.laDichVuThem = true;
        extra.checkedIn = d.checkedIn;
        extra.nhanVienId = d.nhanVienId;
        extra.canNangThucTe = d.canNangThucTe;
        b.chiTietDonList.push(extra);
        b.tongTienTamTinh += extra.donGiaChot;
        b.trangThai = deriveBookingStatus(b.chiTietDonList, b.trangThai);
      }
      if (p.decision === "APPROVED" && r.type === "REFUND") {
        const refunded = s.ledger
          .filter((l) => l.bookingId === b.id && l.type === "REFUND")
          .reduce((n, l) => n + l.amount, 0);
        assert(
          Number.isSafeInteger(Number(p.amount)) &&
            p.amount > 0 &&
            p.amount <= b.hoaDon.daThanhToan - refunded,
          "Mức hoàn phải dương và không vượt tiền đã thu còn lại.",
        );
        s.ledger.push({
          id: id(),
          bookingId: b.id,
          requestId: r.id,
          type: "REFUND",
          amount: Number(p.amount),
          date: localDate(new Date(now)),
          createdAt: timestamp,
        });
        r.amount = Number(p.amount);
      }
      r.status = p.decision;
      r.resolution = String(p.resolution || "");
      r.updatedAt = timestamp;
      return r;
    }
    case "getFinance":
      allow("FINANCE_MANAGE");
      return {
        bookings: s.bookings,
        requests: s.requests,
        ledger: s.ledger,
        catalog: s.catalog,
        surchargeRules: s.surchargeRules,
      };
    case "updatePrice": {
      allow("FINANCE_MANAGE");
      assert(
        ["services", "combos", "rooms"].includes(p.kind),
        "Loại giá không hợp lệ.",
      );
      assert(
        Number.isSafeInteger(Number(p.price)) && p.price >= 0,
        "Giá phải là số nguyên VND không âm.",
      );
      const entry = find(s.catalog[p.kind], p.id);
      entry[
        p.kind === "services"
          ? "giaCoBan"
          : p.kind === "combos"
            ? "giaCombo"
            : "giaTheoNgay"
      ] = Number(p.price);
      return entry;
    }
    case "updateSurcharge": {
      allow("FINANCE_MANAGE");
      assert(
        p.name?.trim() &&
          Number.isSafeInteger(Number(p.amount)) &&
          p.amount >= 0,
        "Tên/giá phụ phí không hợp lệ.",
      );
      const rule = p.id ? find(s.surchargeRules, p.id) : { id: id() };
      Object.assign(rule, { name: p.name.trim(), amount: Number(p.amount) });
      if (!p.id) s.surchargeRules.push(rule);
      return rule;
    }
    case "resendTracking": {
      const email = String(p.email || "").trim().toLowerCase();
      const response = { message: "Nếu thông tin phù hợp, hệ thống sẽ gửi email. Đây là mô phỏng, chưa gửi SMTP thật." };
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 150) return response;
      // Uniform response and cooldown for both known and unknown addresses.
      const previous = s.resendAttempts[email];
      if (previous !== undefined && now - previous < 60000) return response;
      s.resendAttempts[email] = now;
      for (const [key, time] of Object.entries(s.resendAttempts)) if (now - time > 900000) delete s.resendAttempts[key];
      const trackingCodes = s.bookings.filter((b) =>
        !["COMPLETED", "CANCELLED"].includes(b.trangThai) &&
        String(b.bookingEmail || b.khachHang.email).trim().toLowerCase() === email)
        .map((b) => b.trackingCode);
      if (trackingCodes.length) s.mailOutbox.push({ id: id(), to: email, trackingCodes, createdAt: timestamp });
      return response;
    }
    case "getMailOutbox":
      allow("STAFF_MANAGE");
      return s.mailOutbox;
    case "changeRole": {
      allow("STAFF_MANAGE");
      assert(["ADMIN", "STAFF"].includes(p.role), "Role không hợp lệ.");
      const u = find(s.users, p.id, "Tài khoản");
      assert(!(u.vaiTro === "ADMIN" && u.isActive && p.role === "STAFF" &&
        s.users.filter((x) => x.vaiTro === "ADMIN" && x.isActive).length <= 1), "Cần giữ ít nhất một Admin hoạt động.");
      u.vaiTro = p.role;
      u.workGroup ||= "RECEPTION";
      u.roleChangedAt = timestamp;
      u.roleChangedBy = actor.id;
      return cleanUser(u);
    }
    case "updateVaccinePolicy": {
      allow("SYSTEM_MANAGE");
      const types = ["SPA", "HOTEL", "COMBO_SPA_HOTEL"];
      assert(types.every((type) => typeof p.policy?.[type] === "boolean"), "Cấu hình vaccine không hợp lệ.");
      s.settings.vaccinePolicy = Object.fromEntries(types.map((type) => [type, p.policy[type]]));
      return { ...s.settings.vaccinePolicy };
    }
    case "updateClosedDays": {
      allow("STAFF_MANAGE");
      assert(Number.isFinite(dateNumber(p.week)) && weekDays(p.week)[0] === p.week &&
        p.week >= addDays(weekDays(localDate(new Date(now)))[0], 7), "Chọn tuần tương lai bắt đầu từ thứ Hai.");
      assert(!s.periods.some((period) => period.week === p.week &&
        (period.published || period.openedAt || (period.open && now >= Date.parse(period.openAt)))), "Tuần đã mở form đăng ký; không được sửa ngày nghỉ.");
      const days = weekDays(p.week);
      assert(Array.isArray(p.dates) && p.dates.every((date) => days.includes(date)), "Ngày nghỉ cần thuộc tuần đã chọn.");
      const selected = [...new Set(p.dates)];
      assert(!s.bookings.some((b) => !["CANCELLED", "COMPLETED"].includes(b.trangThai) &&
        (selected.includes(b.date) || b.chiTietDonList.some((d) => hasHotel(d.input) && selected.includes(d.input.checkout)))),
        "Ngày nghỉ xung đột booking nhận/trả đã có. Hãy xử lý lịch hẹn trước.");
      assert(!s.registrations.some((r) => selected.includes(r.date) && r.status !== "REJECTED" && r.purpose !== "HOTEL_CARE"), "Ngày đã có đăng ký ca tiếp nhận.");
      for (const date of days) delete s.closedDays[date];
      for (const date of selected) s.closedDays[date] = { reason: String(p.reason || "Nghỉ tiếp nhận").slice(0, 200), by: actor.id, at: timestamp };
      return s.closedDays;
    }
    case "getSchedule": {
      allow("SCHEDULE_SELF");
      const admin = actor.vaiTro === "ADMIN";
      return {
        periods: s.periods,
        registrations: s.registrations.filter(
          (r) => admin || r.employeeId === actor.id,
        ),
        assignments: s.registrations.filter(
          (r) =>
            r.status === "APPROVED" &&
            (admin ||
              (r.employeeId === actor.id &&
                s.periods.find((p) => p.id === r.periodId)?.published)),
        ),
        users: admin ? s.users.map(cleanUser) : [cleanUser(actor)],
        shifts: s.catalog.shifts,
        closedDays: s.closedDays,
      };
    }
    case "createPeriod": {
      allow("STAFF_MANAGE");
      assert(
        weekDays(p.week)[0] === p.week &&
          p.week >= addDays(weekDays(localDate(new Date(now)))[0], 7),
        "Chọn thứ Hai của tuần làm kế tiếp hoặc sau đó.",
      );
      assert(
        !s.periods.some((x) => x.week === p.week),
        "Tuần đã có đợt đăng ký.",
      );
      assert(
        Number.isInteger(Number(p.capacity)) && p.capacity > 0,
        "Số nhân viên phải lớn hơn 0.",
      );
      assert(
        Number.isFinite(Date.parse(p.openAt)) &&
          Date.parse(p.deadline) > Date.parse(p.openAt),
        "Kiểm tra thời điểm mở và deadline.",
      );
      const period = {
        id: id(),
        week: p.week,
        openAt: p.openAt,
        deadline: p.deadline,
        capacity: Number(p.capacity),
        open: true,
        published: false,
        openedAt: now >= Date.parse(p.openAt) ? timestamp : null,
      };
      s.periods.push(period);
      return period;
    }
    case "togglePeriod": {
      allow("STAFF_MANAGE");
      const period = find(s.periods, p.id);
      assert(!period.published, "Lịch đã chốt.");
      if (period.open && now >= Date.parse(period.openAt)) period.openedAt ||= timestamp;
      period.open = !period.open;
      if (period.open && now >= Date.parse(period.openAt)) period.openedAt ||= timestamp;
      return period;
    }
    case "registerShift": {
      allow("SCHEDULE_SELF");
      const period = find(s.periods, p.periodId);
      assert(
        period.open &&
          !period.published &&
          now >= Date.parse(period.openAt) &&
          now < Date.parse(period.deadline),
        "Đợt đăng ký đã đóng hoặc hết hạn.",
      );
      assert(
        weekDays(period.week).includes(p.date),
        "Ngày nằm ngoài tuần đăng ký.",
      );
      const shift = find(s.catalog.shifts, p.shiftId);
      assert(shift.isActive, "Ca đã ngừng hoạt động.");
      const purpose = p.purpose || "RECEPTION";
      assert(["RECEPTION", "HOTEL_CARE"].includes(purpose), "Loại ca không hợp lệ.");
      assert(!s.closedDays[p.date] || purpose === "HOTEL_CARE", "Ngày nghỉ chỉ đăng ký ca chăm sóc Hotel.");
      assert(
        !s.registrations.some(
          (r) =>
            r.periodId === period.id &&
            r.employeeId === actor.id &&
            r.date === p.date &&
            r.shiftId === Number(p.shiftId) &&
            r.status !== "REJECTED",
        ),
        "Đăng ký trùng.",
      );
      const r = {
        id: id(),
        periodId: period.id,
        employeeId: actor.id,
        date: p.date,
        shiftId: Number(p.shiftId),
        status: "PENDING",
        purpose,
        createdAt: timestamp,
      };
      s.registrations.push(r);
      return r;
    }
    case "reviewShift": {
      allow("STAFF_MANAGE");
      const r = find(s.registrations, p.id);
      const period = find(s.periods, r.periodId);
      assert(!period.published, "Lịch đã chốt.");
      assert(
        ["APPROVED", "REJECTED"].includes(p.decision),
        "Quyết định không hợp lệ.",
      );
      const employeeId = Number(p.employeeId || r.employeeId);
      if (p.decision === "APPROVED") assignmentValid(s, r, employeeId);
      r.employeeId = employeeId;
      r.status = p.decision;
      return r;
    }
    case "publishSchedule": {
      allow("STAFF_MANAGE");
      const period = find(s.periods, p.id);
      assert(!period.published, "Lịch đã chốt.");
      assert(
        s.registrations.some(
          (r) => r.periodId === period.id && r.status === "APPROVED",
        ),
        "Duyệt ít nhất một ca trước khi chốt.",
      );
      for (const registration of s.registrations.filter(
        (r) => r.periodId === period.id && r.status === "APPROVED",
      )) {
        assignmentValid(s, registration, registration.employeeId);
      }
      period.published = true;
      period.open = false;
      return period;
    }
    case "getStaff":
      allow("STAFF_MANAGE");
      return s.users.map(cleanUser);
    case "createStaff": {
      allow("STAFF_MANAGE");
      assert(
        p.name?.trim() && /^[a-zA-Z0-9_.-]{3,40}$/.test(p.username || ""),
        "Tên hoặc username không hợp lệ.",
      );
      assert(
        !s.users.some(
          (u) => u.tenDangNhap.toLowerCase() === p.username.toLowerCase(),
        ),
        "Username đã tồn tại.",
      );
      assert(
        ["RECEPTION", "SPA"].includes(p.workGroup),
        "Chọn nhóm công việc.",
      );
      assert(p.credential, "Nhập mật khẩu khởi tạo.");
      const u = {
        id: Math.max(...s.users.map((u) => u.id)) + 1,
        tenNhanVien: p.name.trim(),
        tenDangNhap: p.username,
        vaiTro: "STAFF",
        workGroup: p.workGroup,
        isActive: true,
        credential: p.credential,
      };
      s.users.push(u);
      return cleanUser(u);
    }
    case "toggleStaff": {
      allow("STAFF_MANAGE");
      const u = find(s.users, p.id);
      assert(
        u.id !== actor.id && u.vaiTro !== "ADMIN",
        "Không khóa tài khoản Admin.",
      );
      assert(
        !s.bookings.some(
          (b) =>
            !["COMPLETED", "CANCELLED"].includes(b.trangThai) &&
            b.chiTietDonList.some((d) => d.nhanVienId === u.id),
        ),
        "Nhân viên đang có thú cưng được phân công.",
      );
      u.isActive = !u.isActive;
      return cleanUser(u);
    }
    case "updateAccount": {
      assert(actor?.isActive, "Phiên không hợp lệ.");
      assert(
        p.id === undefined || Number(p.id) === actor.id,
        "Không có quyền sửa tài khoản khác.",
      );
      assert(
        p.name?.trim() && /^[a-zA-Z0-9_.-]{3,40}$/.test(p.username || ""),
        "Tên/username không hợp lệ.",
      );
      assert(
        !s.users.some(
          (u) =>
            u.id !== actor.id &&
            u.tenDangNhap.toLowerCase() === p.username.toLowerCase(),
        ),
        "Username đã tồn tại.",
      );
      Object.assign(actor, {
        tenNhanVien: p.name.trim(),
        tenDangNhap: p.username,
        email: p.email || "",
        soDienThoai: p.phone || "",
      });
      if (p.credential) actor.credential = p.credential;
      return cleanUser(actor);
    }
    case "getSettings":
      allow("SYSTEM_MANAGE");
      return {
        shifts: s.catalog.shifts,
        capacity: s.capacity,
        bookings: s.bookings,
        vaccinePolicy: { ...s.settings.vaccinePolicy },
      };
    case "updateCapacity": {
      allow("SYSTEM_MANAGE");
      const r = remaining(s, p.date, p.shiftId);
      assert(
        Number.isInteger(Number(p.total)) && p.total >= r.reserved,
        "Sức chứa không được nhỏ hơn số pet đã giữ chỗ.",
      );
      assert(
        p.date >= localDate(new Date(now)),
        "Chọn ngày hiện tại hoặc tương lai.",
      );
      s.capacity[`${p.date}:${p.shiftId}`] = Number(p.total);
      return remaining(s, p.date, p.shiftId);
    }
    case "updateShift": {
      allow("SYSTEM_MANAGE");
      const shift = find(s.catalog.shifts, p.id);
      assert(
        /^\d{2}:\d{2}$/.test(p.start || "") &&
          /^\d{2}:\d{2}$/.test(p.end || "") &&
          p.start < p.end &&
          p.end <= "23:59",
        "Giờ bắt đầu/kết thúc không hợp lệ.",
      );
      assert(
        !s.registrations.some(
          (r) =>
            r.shiftId === shift.id &&
            s.periods.find((x) => x.id === r.periodId)?.published,
        ),
        "Ca thuộc lịch đã chốt; giữ giờ hiện tại.",
      );
      assert(
        !s.bookings.some(
          (b) =>
            b.shiftId === shift.id &&
            !["CANCELLED", "COMPLETED"].includes(b.trangThai),
        ),
        "Ca đang có booking; giữ giờ hiện tại.",
      );
      const candidate = {
        ...s,
        catalog: {
          ...s.catalog,
          shifts: s.catalog.shifts.map((current) =>
            current.id === shift.id
              ? { ...current, gioBatDau: p.start, gioKetThuc: p.end }
              : current,
          ),
        },
      };
      for (const registration of s.registrations.filter(
        (r) =>
          r.status === "APPROVED" &&
          !s.periods.find((period) => period.id === r.periodId)?.published,
      )) {
        assignmentValid(candidate, registration, registration.employeeId);
      }
      shift.gioBatDau = p.start;
      shift.gioKetThuc = p.end;
      return shift;
    }
    default:
      throw new Error("Thao tác chưa được hỗ trợ.");
  }
}

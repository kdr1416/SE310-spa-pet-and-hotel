import httpClient from "./httpClient";
import { mockCaLamViec } from "./mockData";
import { localDate } from "../../utils/dates";

function validationError(field, message) {
  return {
    success: false,
    data: null,
    message,
    errorCode: "VALIDATION_FAILED",
    errors: [{ field, message }],
  };
}

async function projectResponse(request, project) {
  const envelope = await request;
  if (!envelope.success || envelope.data == null) return envelope;
  return { ...envelope, data: project(envelope.data) };
}

function projectCatalog(catalog) {
  return {
    ...catalog,
    services: catalog.dichVu.map((service) => ({
      ...service, id: service.maDichVu, isActive: service.dangHoatDong,
    })),
    combos: catalog.combo.map((combo) => ({
      ...combo, id: combo.maCombo, isActive: combo.dangHoatDong,
    })),
    rooms: catalog.loaiPhong.map((room) => ({
      ...room, id: room.maLoaiPhong, isActive: room.dangHoatDong, giaTheoNgay: room.giaMoiDem,
    })),
    shifts: mockCaLamViec.slice(0, 2).map(({ id, tenCa }) => ({ id, tenCa })),
    availability: [],
    surchargeRules: [],
  };
}

function checkAvailability({ maKhungGio, ngayTra, thuCung }) {
  return httpClient.post("/availability", {
    maKhungGio,
    ngayTra,
    thuCung: thuCung?.map(({ maLoaiPhong, dichVu }) => ({
      maLoaiPhong,
      dichVu: dichVu?.map(({ maDichVu, maCombo, maKhungGio }) => ({
        maDichVu, maCombo, maKhungGio,
      })),
    })),
  });
}

function projectInvoice(invoice) {
  return {
    ...invoice,
    id: invoice.maHoaDon,
    trangThaiThanhToan: invoice.trangThai,
    tongThanhToan: invoice.conPhaiTra,
    ctHoaDonList: invoice.dongTien.map((line) => ({
      ...line, id: line.maDongHoaDon, tenKhoanMuc: line.tenKhoan,
    })),
  };
}

function projectBookingPet(pet) {
  const checkedIn = pet.canNangChot != null;
  return {
    ...pet,
    id: pet.maChiTietDon,
    thuCung: { id: pet.maThuCung, tenThuCung: pet.tenThuCung },
    input: {},
    label: pet.dichVu.map(({ tenDichVu }) => tenDichVu).join(" + "),
    checkedIn,
    canNangThucTe: pet.canNangChot,
    trangThaiChamSoc: pet.trangThai,
    nhatKyChamSoc: pet.nhatKy.map((log) => ({
      ...log,
      id: log.maNhatKy,
      photo: log.anhUrl,
      ghiChuSucKhoe: log.ghiChu,
      thoiGianGhiNhan: log.ghiNhanAt,
    })),
  };
}

function projectBooking(booking, verifiedPhone) {
  const contact = booking.lienHe ?? booking.hoaDon?.khach;
  return {
    ...booking,
    id: booking.maDon,
    date: localDate(new Date(booking.batDauDuKien)),
    endDate: localDate(new Date(booking.ketThucDuKien)),
    thoiGianBatDau: booking.batDauDuKien,
    tongTienTamTinh: booking.tongTamTinh,
    khachHang: contact
      ? { ...contact, tenKhachHang: contact.hoTen }
      : { soDienThoai: verifiedPhone },
    chiTietDonList: booking.thuCung.map(projectBookingPet),
    hoaDon: booking.hoaDon && projectInvoice(booking.hoaDon),
  };
}

async function lookupTracking(payload, projectForUi = false) {
  const phone = payload.soDienThoai ?? payload.phone;
  const soDienThoai = typeof phone === "string" ? phone.replace(/[\s-]/g, "") : "";
  if (!/^[0-9]{10}$/.test(soDienThoai)) {
    throw validationError("soDienThoai", "Số điện thoại cần có 10 chữ số.");
  }

  const trackingCode = projectForUi && typeof payload.trackingCode === "string"
    ? payload.trackingCode.trim().toUpperCase()
    : payload.trackingCode;
  const request = httpClient.post("/tracking/lookup", { soDienThoai, trackingCode });
  return projectForUi
    ? projectResponse(request, (booking) => projectBooking(booking, soDienThoai))
    : request;
}

function listBookings({ trang, kichThuocTrang, tuNgay, denNgay, trangThai }) {
  return httpClient.get("/internal/bookings", {
    params: { trang, kichThuocTrang, tuNgay, denNgay, trangThai },
  });
}

function getWorkspace({ kichThuocTrang = 100, ...query }) {
  return projectResponse(
    listBookings({ ...query, kichThuocTrang }),
    ({ items, trang, kichThuocTrang, tongSo }) => ({
      bookings: items.map((booking) => projectBooking(booking)),
      users: [],
      requests: [],
      trang,
      kichThuocTrang,
      tongSo,
    }),
  );
}

function getInvoice({ invoiceId }) {
  if (!Number.isInteger(invoiceId) || invoiceId <= 0) {
    throw validationError("invoiceId", "Cần mã hóa đơn hợp lệ.");
  }
  return httpClient.get(`/internal/invoices/${invoiceId}`);
}

const operations = new Map([
  ["getCatalog", { mutation: false, call: () => httpClient.get("/catalog") }],
  ["catalog", { mutation: false, call: () => projectResponse(httpClient.get("/catalog"), projectCatalog) }],
  ["checkAvailability", { mutation: false, call: checkAvailability }],
  ["lookupTracking", { mutation: false, call: lookupTracking }],
  ["lookupBooking", { mutation: false, call: (payload) => lookupTracking(payload, true) }],
  ["listBookings", { mutation: false, call: listBookings }],
  ["getWorkspace", { mutation: false, call: getWorkspace }],
  ["getInvoice", { mutation: false, call: getInvoice }],
]);

export function supportsSprint1Action(action) {
  return operations.has(action);
}

export function isSprint1Mutation(action) {
  return operations.get(action)?.mutation === true;
}

export async function callSprint1Operation(action, payload = {}) {
  return operations.get(action).call(payload);
}

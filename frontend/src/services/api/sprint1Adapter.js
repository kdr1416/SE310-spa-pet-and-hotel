import httpClient from "./httpClient";
import { mockCaLamViec } from "./mockData";
import { localDate } from "../../utils/dates";

function validationError(field, message, errorCode = "VALIDATION_FAILED") {
  return {
    success: false,
    data: null,
    message,
    errorCode,
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

function requireSlotId(slotId, field) {
  if (!Number.isInteger(slotId) || slotId <= 0) {
    throw validationError(field, "Cần mã khung giờ đặt lịch hợp lệ.", "MISSING_SLOT_MAPPING");
  }
}

function checkAvailability({ maKhungGio, ngayTra, thuCung }) {
  requireSlotId(maKhungGio, "maKhungGio");
  return httpClient.post("/availability", {
    maKhungGio,
    ngayTra,
    thuCung: thuCung?.map(({ maLoaiPhong, dichVu }, petIndex) => ({
      maLoaiPhong,
      dichVu: dichVu?.map(({ maDichVu, maCombo, maKhungGio }, serviceIndex) => {
        requireSlotId(maKhungGio, `thuCung[${petIndex}].dichVu[${serviceIndex}].maKhungGio`);
        return { maDichVu, maCombo, maKhungGio };
      }),
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

function normalizePhone(phone) {
  const normalized = typeof phone === "string" ? phone.replace(/[\s-]/g, "") : "";
  if (!/^[0-9]{10}$/.test(normalized)) {
    throw validationError("soDienThoai", "Số điện thoại cần có 10 chữ số.");
  }
  return normalized;
}

async function lookupTracking(payload, projectForUi = false) {
  const soDienThoai = normalizePhone(payload.soDienThoai ?? payload.phone);
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

function ensureRequestId(payload) {
  payload.requestId = payload.requestId || crypto.randomUUID();
  return payload.requestId;
}

function toBookingServiceDto(selection, bookingSlotId, field) {
  const { maDichVu, maCombo } = selection;
  const maKhungGio = selection.maKhungGio ?? bookingSlotId;
  requireSlotId(maKhungGio, `${field}.maKhungGio`);
  if ((maDichVu == null) === (maCombo == null)) {
    throw validationError(field, "Chọn đúng một dịch vụ hoặc combo.");
  }
  return { maDichVu, maCombo, maKhungGio };
}

function toBookingPetDto(pet, bookingSlotId, petIndex) {
  const selections = pet.dichVu ?? (pet.type === "HOTEL" ? [] : [{
    maDichVu: pet.serviceId,
    maCombo: pet.comboId,
    maKhungGio: pet.maKhungGio,
  }]);
  return {
    maThuCung: pet.maThuCung,
    tenThuCung: pet.tenThuCung ?? pet.name,
    loaiPet: pet.loaiPet ?? pet.species,
    giong: pet.giong ?? pet.breed,
    canNangKhaiBao: pet.canNangKhaiBao ?? pet.weight,
    loaiLong: pet.loaiLong ?? pet.coatType,
    ghiChuDiUng: pet.ghiChuDiUng ?? pet.notes,
    maLoaiPhong: pet.maLoaiPhong ?? (pet.type === "SPA" ? undefined : pet.roomId),
    dichVu: selections.map((selection, index) =>
      toBookingServiceDto(selection, bookingSlotId, `thuCung[${petIndex}].dichVu[${index}]`),
    ),
    maHoSoTiemChung: pet.maHoSoTiemChung,
  };
}

function toBookingDto(payload, vaccinationFiles) {
  const { maKhungGio, lienHe, customer } = payload;
  requireSlotId(maKhungGio, "maKhungGio");
  const contact = {
    hoTen: lienHe?.hoTen ?? customer?.name,
    soDienThoai: normalizePhone(lienHe?.soDienThoai ?? customer?.phone),
    email: lienHe?.email ?? customer?.email,
  };

  const pets = payload.thuCung ?? payload.pets ?? [];
  const namedFiles = payload.files ?? {};
  const explicitKeys = new Set(pets.map((pet) => pet.vaccinationFileKey));
  const thuCung = pets.map((pet, index) => {
    const dto = toBookingPetDto(pet, maKhungGio, index);
    const key = pet.vaccinationFileKey;
    const namedFile = key != null && Object.hasOwn(namedFiles, key) ? namedFiles[key] : undefined;
    const file = pet.vaccinationFile ?? pet.vaccine ?? namedFile;
    if (key == null && file == null) return dto;

    if (!(file instanceof File) || (key != null && (typeof key !== "string" || !key.trim()))) {
      throw validationError(`thuCung[${index}].vaccinationFileKey`, "Ảnh giấy tiêm chủng cần khớp mã đính kèm.");
    }

    let fileKey = key;
    if (fileKey == null) {
      fileKey = `vaccination-${index}`;
      while (explicitKeys.has(fileKey)) fileKey += "-file";
    }
    if (vaccinationFiles.has(fileKey)) {
      throw validationError(`thuCung[${index}].vaccinationFileKey`, "Mỗi ảnh giấy tiêm chủng cần mã đính kèm riêng.");
    }
    vaccinationFiles.set(fileKey, file);
    return { ...dto, vaccinationFileKey: fileKey };
  });

  return {
    requestId: ensureRequestId(payload),
    lienHe: contact,
    maKhungGio,
    ngayTra: payload.ngayTra ?? payload.checkout,
    thuCung,
    ghiChu: payload.ghiChu,
    bookingProof: payload.bookingProof,
    petProof: payload.petProof,
    maKhachHang: payload.maKhachHang,
  };
}

function createBooking(payload) {
  const vaccinationFiles = new Map();
  const dto = toBookingDto(payload, vaccinationFiles);
  const form = new FormData();
  // The E04 multipart parser requires optional fields to be omitted when they have no value.
  form.append("payload", JSON.stringify(dto, (_key, value) => value ?? undefined));
  for (const [key, file] of vaccinationFiles) form.append(key, file);

  const request = httpClient.post("/bookings", form);
  const isUiPayload = payload.thuCung == null && payload.pets != null;
  return isUiPayload ? projectResponse(request, (booking) => projectBooking(booking)) : request;
}

function requireBookingId({ bookingId, id }) {
  const resolvedId = bookingId ?? id;
  if (!Number.isInteger(resolvedId) || resolvedId <= 0) {
    throw validationError("bookingId", "Cần mã đơn hợp lệ.");
  }
  return resolvedId;
}

function confirmBooking(payload) {
  const bookingId = requireBookingId(payload);
  return httpClient.post(`/internal/bookings/${bookingId}/confirm`, {
    requestId: ensureRequestId(payload),
  });
}

function cancelBooking(payload) {
  const bookingId = requireBookingId(payload);
  const lyDo = payload.lyDo ?? payload.reason;
  if (typeof lyDo !== "string" || !lyDo.trim()) {
    throw validationError("lyDo", "Cần lý do hủy đơn.");
  }
  return httpClient.post(`/internal/bookings/${bookingId}/cancel`, {
    requestId: ensureRequestId(payload),
    loaiHuy: payload.loaiHuy ?? "STAFF_REJECTED",
    lyDo,
  });
}

function reviewBooking(payload) {
  switch (payload.decision) {
    case "CONFIRMED":
      return projectResponse(confirmBooking(payload), (booking) => projectBooking(booking));
    case "CANCELLED":
      return projectResponse(cancelBooking(payload), (booking) => projectBooking(booking));
    default:
      throw validationError("decision", "Chỉ có thể duyệt hoặc hủy đơn.");
  }
}

const operations = new Map([
  ["getCatalog", { mutation: false, call: () => httpClient.get("/catalog") }],
  ["catalog", { mutation: false, call: () => projectResponse(httpClient.get("/catalog"), projectCatalog) }],
  ["checkAvailability", { mutation: false, call: checkAvailability }],
  ["createBooking", { mutation: true, call: createBooking }],
  ["confirmBooking", { mutation: true, call: confirmBooking }],
  ["cancelBooking", { mutation: true, call: cancelBooking }],
  ["reviewBooking", { mutation: true, call: reviewBooking }],
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

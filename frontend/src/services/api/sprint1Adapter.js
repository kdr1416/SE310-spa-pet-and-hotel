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
  const legacySpaOnly = payload.thuCung == null && pets.length > 0
    && pets.every((pet) => pet.type === "SPA" && pet.maLoaiPhong == null);
  let ngayTra = payload.ngayTra ?? payload.checkout;
  if (payload.ngayTra == null && legacySpaOnly) {
    // The legacy form keeps a next-day checkout value hidden for Spa-only bookings.
    ngayTra = payload.date;
    if (typeof ngayTra !== "string" || !ngayTra.trim()) {
      throw validationError("ngayTra", "Cần ngày tiếp nhận cho lịch chỉ đặt dịch vụ Spa.");
    }
  }

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
    ngayTra,
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

function requirePetDetailId({ maChiTietDon, detailId, id }, isUiAlias = false) {
  const petId = maChiTietDon ?? detailId ?? (isUiAlias ? undefined : id);
  if (!Number.isInteger(petId) || petId <= 0) {
    throw validationError("maChiTietDon", "Cần mã chi tiết thú cưng hợp lệ.");
  }
  return petId;
}

function hasText(value) {
  return typeof value === "string" && Boolean(value.trim());
}

function postCareMultipart(url, dto, photoField, photo) {
  const form = new FormData();
  form.append("payload", JSON.stringify(dto, (_key, value) => value ?? undefined));
  if (photo != null) form.append(photoField, photo);
  return httpClient.post(url, form);
}

function toCheckinDto(payload) {
  const canNangThucTe = payload.canNangThucTe ?? payload.weight;
  const maNhanVien = payload.maNhanVien ?? payload.employeeId;
  const furInput = payload.tinhTrangLong ?? payload.coat;
  const tinhTrangLong = typeof furInput === "string" && /^[A-Za-z0-9_-]+$/.test(furInput)
    ? [furInput]
    : furInput;
  const { ghiChuSucKhoe, quyetDinhGia, xacNhanKhach, noiDungThuGon, maHoSoTiemChung, xacNhanGiayHopLe } = payload;

  if (!Number.isFinite(canNangThucTe) || canNangThucTe <= 0) {
    throw validationError("canNangThucTe", "Cần cân nặng thực tế hợp lệ.");
  }
  if (!Array.isArray(tinhTrangLong) || !tinhTrangLong.length || !tinhTrangLong.every(hasText)) {
    throw validationError("tinhTrangLong", "Cần mã tình trạng lông.");
  }
  if (!Number.isInteger(maNhanVien) || maNhanVien <= 0) {
    throw validationError("maNhanVien", "Cần chọn nhân viên phụ trách.");
  }
  if (!hasText(ghiChuSucKhoe)) {
    throw validationError("ghiChuSucKhoe", "Cần ghi nhận sức khỏe lúc tiếp nhận.");
  }
  if (!["SIZE_THUC_TE", "THU_GON"].includes(quyetDinhGia)) {
    throw validationError("quyetDinhGia", "Cần quyết định về giá lúc tiếp nhận.");
  }
  if (quyetDinhGia === "THU_GON" && !hasText(noiDungThuGon)) {
    throw validationError("noiDungThuGon", "Cần ghi nội dung thu gọn dịch vụ.");
  }
  if (xacNhanKhach !== true) {
    throw validationError("xacNhanKhach", "Cần xác nhận đồng thuận của khách.");
  }

  return {
    requestId: ensureRequestId(payload),
    canNangThucTe, tinhTrangLong, maNhanVien, ghiChuSucKhoe, quyetDinhGia,
    xacNhanKhach, noiDungThuGon, maHoSoTiemChung, xacNhanGiayHopLe,
  };
}

function checkInPet(payload, isUiAlias = false) {
  const petId = requirePetDetailId(payload, isUiAlias);
  const photo = payload.anhTiepNhan;
  if (!(photo instanceof File)) {
    throw validationError("anhTiepNhan", "Cần ảnh lúc tiếp nhận thú cưng.");
  }
  const request = postCareMultipart(
    `/internal/booking-pets/${petId}/check-in`, toCheckinDto(payload), "anhTiepNhan", photo,
  );
  return isUiAlias ? projectResponse(request, projectBookingPet) : request;
}

function careActionFromState({ next, currentState }) {
  switch (next) {
    case "IN_PROGRESS":
      if (currentState === "WAITING") return "START";
      if (currentState === "PAUSED") return "RESUME";
      throw validationError("currentState", "Cần trạng thái chờ hoặc tạm dừng của thú cưng.");
    case "PAUSED":
      return "PAUSE";
    case "READY":
      return "READY";
  }
}

function performCareAction(payload, isUiAlias = false) {
  const petId = requirePetDetailId(payload, isUiAlias);
  const hanhDong = payload.hanhDong ?? (isUiAlias ? careActionFromState(payload) : undefined);
  const ghiChu = payload.ghiChu ?? payload.note;
  const photo = payload.anhThanhPham ?? payload.photo;
  const { maNgoaiLeAnh } = payload;

  if (!["START", "PAUSE", "RESUME", "READY"].includes(hanhDong)) {
    throw validationError("hanhDong", "Cần hành động chăm sóc hợp lệ.");
  }
  const hasNote = hasText(ghiChu);
  if (hanhDong === "PAUSE" && !hasNote) {
    throw validationError("ghiChu", "Cần lý do tạm dừng chăm sóc.");
  }
  if (photo != null && !(photo instanceof File)) {
    throw validationError("anhThanhPham", "Cần tệp ảnh thành phẩm hợp lệ.");
  }
  if (hanhDong === "READY" && photo == null && !(Number.isInteger(maNgoaiLeAnh) && maNgoaiLeAnh > 0)) {
    throw validationError("anhThanhPham", "Cần ảnh thành phẩm hoặc mã ngoại lệ ảnh.");
  }

  const dto = {
    requestId: ensureRequestId(payload),
    hanhDong,
    ghiChu: hasNote ? ghiChu : undefined,
    maNgoaiLeAnh,
  };
  const request = postCareMultipart(`/internal/booking-pets/${petId}/care-actions`, dto, "anhThanhPham", photo);
  return isUiAlias ? projectResponse(request, projectBookingPet) : request;
}

function addCareLog(payload) {
  const petId = requirePetDetailId(payload);
  const { anh, ghiChu, canNangTheoDoi } = payload;
  if (anh != null && !(anh instanceof File)) {
    throw validationError("anh", "Cần tệp ảnh nhật ký hợp lệ.");
  }
  const hasNote = hasText(ghiChu);
  if (!hasNote && anh == null) {
    throw validationError("ghiChu", "Cần ghi chú hoặc ảnh nhật ký.");
  }
  const dto = {
    requestId: ensureRequestId(payload),
    ghiChu: hasNote ? ghiChu : undefined,
    canNangTheoDoi,
  };
  return postCareMultipart(`/internal/booking-pets/${petId}/care-logs`, dto, "anh", anh);
}

function handoverPet(payload) {
  const petId = requirePetDetailId(payload);
  const { xacNhanDaNhan } = payload;
  if (xacNhanDaNhan !== true) {
    throw validationError("xacNhanDaNhan", "Cần xác nhận khách đã nhận thú cưng.");
  }
  return httpClient.post(`/internal/booking-pets/${petId}/handover`, {
    requestId: ensureRequestId(payload),
    xacNhanDaNhan,
  });
}

function createInvoice(payload) {
  const bookingId = requireBookingId(payload);
  return httpClient.post(`/internal/bookings/${bookingId}/invoices`, {
    requestId: ensureRequestId(payload),
  });
}

function collectInvoice(payload, invoiceId) {
  const phuongThuc = payload.phuongThuc ?? payload.method;
  const { soTienThucNhan, soTienDaTraLai, thamChieu, bangChungTraThua } = payload;
  if (!Number.isInteger(invoiceId) || invoiceId <= 0) {
    throw validationError("invoiceId", "Cần mã hóa đơn hợp lệ. Hãy lập hóa đơn trước khi thu tiền.");
  }
  if (!["CASH", "BANK_TRANSFER"].includes(phuongThuc)) {
    throw validationError("phuongThuc", "Cần phương thức thanh toán hợp lệ.");
  }
  if (!Number.isFinite(soTienThucNhan)) {
    throw validationError("soTienThucNhan", "Cần nhập số tiền thực nhận.");
  }
  if (!Number.isFinite(soTienDaTraLai)) {
    throw validationError("soTienDaTraLai", "Cần nhập số tiền đã trả lại.");
  }
  return httpClient.post(`/internal/invoices/${invoiceId}/checkout`, {
    requestId: ensureRequestId(payload),
    phuongThuc, soTienThucNhan, soTienDaTraLai, thamChieu, bangChungTraThua,
  });
}

function checkoutInvoice(payload) {
  return collectInvoice(payload, payload.invoiceId ?? payload.maHoaDon ?? payload.id);
}

function confirmPayment(payload) {
  const invoiceId = payload.invoiceId ?? payload.maHoaDon ?? payload.hoaDon?.maHoaDon;
  return projectResponse(collectInvoice(payload, invoiceId), projectInvoice);
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
  ["checkInPet", { mutation: true, call: checkInPet }],
  ["checkIn", { mutation: true, call: (payload) => checkInPet(payload, true) }],
  ["performCareAction", { mutation: true, call: performCareAction }],
  ["careUpdate", { mutation: true, call: (payload) => performCareAction(payload, true) }],
  ["addCareLog", { mutation: true, call: addCareLog }],
  ["handoverPet", { mutation: true, call: handoverPet }],
  ["createInvoice", { mutation: true, call: createInvoice }],
  ["checkoutInvoice", { mutation: true, call: checkoutInvoice }],
  ["confirmPayment", { mutation: true, call: confirmPayment }],
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

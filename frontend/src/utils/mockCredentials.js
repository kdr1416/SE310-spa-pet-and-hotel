const hex = (bytes) =>
  Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
export async function hashMockPassword(
  password,
  salt = crypto.getRandomValues(new Uint8Array(16)),
) {
  if (typeof password !== "string" || password.length < 8)
    throw new Error("Mật khẩu cần ít nhất 8 ký tự.");
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    "PBKDF2",
    false,
    ["deriveBits"],
  );
  const hash = await crypto.subtle.deriveBits(
    { name: "PBKDF2", salt, iterations: 100000, hash: "SHA-256" },
    key,
    256,
  );
  return { salt: hex(salt), hash: hex(new Uint8Array(hash)) };
}
export async function matchesMockPassword(password, credential) {
  if (!credential) return false;
  try {
    return (
      (
        await hashMockPassword(
          password,
          new Uint8Array(
            credential.salt.match(/.{2}/g).map((x) => parseInt(x, 16)),
          ),
        )
      ).hash === credential.hash
    );
  } catch {
    return false;
  }
}

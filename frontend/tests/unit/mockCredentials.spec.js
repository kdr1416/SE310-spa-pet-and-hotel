import { it, expect } from "vitest";
import {
  hashMockPassword,
  matchesMockPassword,
} from "../../src/utils/mockCredentials";
it("stores a salted derivation, verifies the right input and rejects a different input", async () => {
  const input = crypto.randomUUID(),
    other = crypto.randomUUID();
  const a = await hashMockPassword(input),
    b = await hashMockPassword(input);
  expect(a.hash).not.toBe(input);
  expect(a.hash).not.toBe(b.hash);
  expect(await matchesMockPassword(input, a)).toBe(true);
  expect(await matchesMockPassword(other, a)).toBe(false);
});

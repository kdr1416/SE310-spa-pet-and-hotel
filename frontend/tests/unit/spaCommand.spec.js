import { expect, it, vi } from "vitest";
import { useSpaCommand } from "../../src/composables/useSpaCommand";
it("retains the logical key after a known rejection, changing it only when input changes", async () => {
  const command = useSpaCommand();
  const send = vi.fn().mockRejectedValue({ errorCode: "INVALID_AMOUNT" });
  await expect(command.run({ invoiceId: 902, amount: 10 }, send)).rejects.toMatchObject({ errorCode: "INVALID_AMOUNT" });
  await expect(command.run({ invoiceId: 902, amount: 10 }, send)).rejects.toMatchObject({ errorCode: "INVALID_AMOUNT" });
  expect(send.mock.calls[1][0].requestId).toBe(send.mock.calls[0][0].requestId);
  await expect(command.run({ invoiceId: 902, amount: 20 }, send)).rejects.toMatchObject({ errorCode: "INVALID_AMOUNT" });
  expect(send.mock.calls[2][0].requestId).not.toBe(send.mock.calls[1][0].requestId);
});

import { readFile, writeFile } from "node:fs/promises";
import { createHash, randomUUID } from "node:crypto";
import assert from "node:assert/strict";
const [mode, base, path] = process.argv.slice(2);
const digest = (body: string) =>
  createHash("sha256").update(body).digest("hex");
async function request(url: string, init?: RequestInit) {
  const response = await fetch(base + url, init);
  const value = await response.json();
  assert.equal(response.status, 200, JSON.stringify(value));
  return value;
}
if (mode === "prepare") {
  const metadata = {
    protocol: "salyra-upload/1",
    name: "restart.txt",
    size: 8,
    type: "text/plain",
    lastModified: 0,
    chunkSize: 4,
  };
  const key = randomUUID();
  const session = await request("", {
    method: "POST",
    headers: { "Content-Type": "application/json", "Idempotency-Key": key },
    body: JSON.stringify(metadata),
  });
  await request(`/${session.id}/parts/0`, {
    method: "PUT",
    headers: { "Upload-Checksum": digest("abcd") },
    body: "abcd",
  });
  await writeFile(path, JSON.stringify({ key, metadata, session }));
} else if (mode === "recover") {
  const { key, metadata, session } = JSON.parse(await readFile(path, "utf8"));
  const recovered = await request("", {
    method: "POST",
    headers: { "Content-Type": "application/json", "Idempotency-Key": key },
    body: JSON.stringify(metadata),
  });
  assert.equal(recovered.id, session.id);
  const checkpoint = await request(`/${session.id}`);
  assert.equal(checkpoint.parts.length, 1);
  assert.equal(checkpoint.parts[0].sha256, digest("abcd"));
  await request(`/${session.id}/parts/1`, {
    method: "PUT",
    headers: { "Upload-Checksum": digest("efgh") },
    body: "efgh",
  });
  const completed = await request(`/${session.id}/complete`, {
    method: "POST",
  });
  assert.equal(completed.size, 8);
  assert.deepEqual(
    await request(`/${session.id}/complete`, { method: "POST" }),
    completed,
  );
  console.log(`Persistent recovery passed for ${base}`);
} else
  throw new Error("Use prepare/recover, the base URL and the fixture path");

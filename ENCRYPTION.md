# Encrypted upload storage

The Node server can encrypt file contents on disk without changing the browser transport. The application provides the keys. The browser still sends plaintext over HTTPS and the server sees the file contents.

Use `@salyra-ui/upload-server/encryption` for the encrypted filesystem adapter. Use the `encryption` option on `s3Storage` for AWS server-side encryption. These are separate storage implementations, not browser end-to-end encryption.

## Keys and storage

```ts
import { encryptedFilesystemStorage } from "@salyra-ui/upload-server/encryption";

// Provision once in your secret manager, then load the same key after restart.
// randomBytes(32) illustrates key generation. Do not generate your storage key on every boot.
const key = await secrets.getBytes("uploads/key-v1");
const storage = encryptedFilesystemStorage({
  directory: "./private-uploads/encrypted",
  keys: {
    current: () => ({ id: "key-v1", key }),
    resolve: (id) => (id === "key-v1" ? key : undefined),
  },
});
```

The `secrets` object belongs to your application. A key contains exactly 32 bytes and can be a `Uint8Array`, `Buffer` or Node secret `KeyObject`. No key is written to the upload directory, session ledger or result.

| Key                               | Accepted values                                            | What it does                                                                                                                                       |
| --------------------------------- | ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `directory`                       | string                                                     | Private directory on one filesystem. Temporary content is under `parts`, committed content under `files`.                                          |
| `keys.current(context)`           | `{ id: string, key: EncryptionKeyMaterial }`, or a Promise | Selects the key for a new upload session. The ID is nonempty and at most 128 characters.                                                           |
| `keys.resolve(id, context)`       | Key material, `undefined`, or a Promise                    | Loads the pinned key for resume, reconciliation, completion and download. Returning undefined fails with `ENCRYPTION_KEY_MISSING`.                 |
| `storage.read(id, context?)`      | `Promise<Readable>`                                        | Assembles and decrypts a committed upload. The application authorizes the download before calling it. Stream errors must be handled by the caller. |
| `storage.remove(result, context)` | `Promise<void>`                                            | Deletes a committed encrypted upload. It does not need the decryption key.                                                                         |
| `result.encryption`               | `{ format: 'salyra-aes256gcm/1', keyId: string }`          | Describes the storage format and pinned key ID. The result also has `id`, plaintext `size` and plaintext `sha256`.                                 |

Pass this adapter as `storage` to `createUploadServer`. Keep the session store separate. Chunk size, checksums and upload progress describe plaintext bytes, just as with the ordinary filesystem adapter. Chunk sizes range from 1 byte to 64 MiB, with at most 10,000 chunks.

## Rotation and recovery

Make the new key available through `resolve` before making it current. Keep old versions available for existing sessions and completed files. An upload that started with `key-v1` continues with `key-v1` after restart, even when `current` returns `key-v2`.

Rotation changes the key for new sessions. It does not re-encrypt existing files. Removing a key makes its files unreadable until the application restores it. Losing the key permanently loses access to those file contents. Missing keys never trigger a plaintext fallback or automatic deletion.

Each request passes the server context to the key callbacks. Applications can use this to select a tenant's keys. The upload engine's authorization callbacks and download access checks remain necessary.

Cancel deletes temporary encrypted content. Retry after Cancel creates a fresh session through the normal uploader lifecycle. Reset discards the resume checkpoint. Completed files are removed through the application's removal endpoint.

## Downloads

```ts
import { pipeline } from "node:stream/promises";

const context = await getApplicationContext(request);
await authorizeDocumentDownload(document, context);
const body = await storage.read(document.uploadId, context);
response.setHeader("Content-Type", "application/octet-stream");
response.setHeader("Content-Length", document.size);
try {
  await pipeline(body, response);
} catch (error) {
  // pipeline closes the response on a failed authentication tag or disconnected client.
  reportDownloadFailure(error);
}
```

The document lookup, authorization and logging functions belong to your application. Do not expose the storage directory as static files. `read` authenticates each frame before returning it. Previously verified frames can already have reached the client when a later frame fails, so callers must treat an interrupted download as incomplete.

## AWS and Cloudflare

```ts
const storage = s3Storage({
  client,
  bucket: "documents",
  journal,
  encryption: {
    mode: "aws:kms",
    keyId: "alias/document-uploads",
    bucketKey: true,
  },
});
// Or encryption: { mode: 'AES256' }
```

The settings are passed to multipart creation and empty-file creation. AWS owns encryption and decryption for these objects. Configure the KMS policy and S3 permissions in your application infrastructure. Changing this option affects newly created objects, not existing multipart sessions. It does not use the encrypted filesystem format or `storage.read`.

`r2Storage` rejects AWS encryption options. R2 encrypts objects at rest through its own service. Native backend storage interfaces can implement encryption or use their provider's storage encryption. This release includes the encrypted disk adapter in Node only.

## Storage format v1

This section documents the on-disk format, not an additional browser protocol.

- A session reference records the format, session ID, key ID, random 32-byte file salt, plaintext file size and chunk size. Filenames and application metadata stay in the session ledger.
- HKDF-SHA-256 derives a 32-byte file key from the application key and file salt. Its info string is `salyra/storage/1/<session-id>`.
- Each encrypted chunk starts with the eight ASCII bytes `SLYRAE01` and a fresh random 32-byte attempt salt. HKDF-SHA-256 derives a part key from the file key and attempt salt, with info `salyra/part/1/<part-index>`. Retried writes use fresh salts.
- Plaintext is divided into 64 KiB frames. The last frame may be smaller. An empty chunk has one empty authenticated frame. Every frame uses AES-256-GCM with a 16-byte authentication tag and a 12-byte nonce containing the frame index as an unsigned big-endian integer in the last four bytes.
- The AAD is the UTF-8 JSON array `[format, sessionId, keyId, fileSalt, fileSize, chunkSize, partIndex, frameIndex, frameLength]`. Frame ciphertext is followed immediately by its tag. Frame lengths come from the reference, so no untrusted record length controls allocation.
- Receipt JSON contains the plaintext SHA-256 and a MAC. HMAC-SHA-256 with the file key authenticates the JSON array `['salyra/receipt/1', sessionId, keyId, fileSalt, fileSize, chunkSize, partIndex, partSize, sha256]`. Probe verifies the MAC and encrypted file length without decrypting every previous chunk on every request. A missing receipt is rebuilt by authenticating and hashing its chunk.
- Finish authenticates and hashes all ordered chunks, then publishes the directory with a rename on the same filesystem. No plaintext combined file is written. `read` presents the combined contents as one stream. A finalizing ledger can recover this committed directory after an interrupted completion response.

Truncated frames, trailing bytes, wrong keys, altered authentication tags and swapped parts fail verification. Incomplete writes use private temporary files and never publish a receipt. The adapter uses private directory and file modes when creating new entries. Restrict access to parent directories and keep the storage root under application control.

File sizes, key IDs, upload descriptors and plaintext checksums are not secret metadata. Storage encryption does not authenticate users, hide filenames or stop the server from reading uploads. Backups need both the encrypted files and their retained keys. This implementation has regression tests, but has not had an independent cryptographic review.

References: [Node crypto](https://nodejs.org/docs/latest-v22.x/api/crypto.html), [S3 multipart encryption](https://docs.aws.amazon.com/AmazonS3/latest/API/API_CreateMultipartUpload.html), [R2 encryption](https://developers.cloudflare.com/r2/reference/data-security/).

## Verification

```sh
npm test
SALYRA_STRESS_ENCRYPTED=1 node --import tsx scripts/stress.ts
```

The stress test uploads a disk-backed 2 GiB file through the real HTTP router, decrypts the completed stream and compares its SHA-256 with the source. It checks an additional-RSS limit of 384 MiB for the combined client and server process. It does not measure browser memory or a remote production deployment.

# Salyra UI Upload Server

Persistent upload sessions with streaming storage and configurable routes. Importing the package does not register routes or start a server.

```sh
npm install @salyra-ui/upload-server
```

```ts
import { createServer } from "node:http";
import {
  createUploadServer,
  createUploadRouter,
} from "@salyra-ui/upload-server";
import {
  filesystemSessionStore,
  filesystemStorage,
} from "@salyra-ui/upload-server/filesystem";

const engine = createUploadServer({
  sessionStore: filesystemSessionStore("./private-uploads/sessions"),
  storage: filesystemStorage("./private-uploads/content"),
  maxFileSize: 128 * 1024 * 1024,
  // Supply authorize and scope from your application's authentication.
});
const upload = createUploadRouter(engine, { basePath: "/documents/uploads" });
createServer(async (request, response) => {
  if (!(await upload(request, response))) {
    response.writeHead(404);
    response.end();
  }
}).listen(3000, "127.0.0.1");
```

Call `createUpload`, `getUpload`, `receivePart`, `finishUpload` and `cancelUpload` directly from your own controllers. Session storage and file storage are independent interfaces.

`/s3` exports `s3Storage` and `r2Storage`. Install `@aws-sdk/client-s3` for these adapters and supply a persistent receipt journal. Provider SDKs are excluded from the core server entry.

[Documentation](https://salyra-ui.github.io/docs/upload-server.html) · [Protocol](https://github.com/salyra-ui/file-upload/blob/main/protocol/README.md)

Notifications run after the storage transaction releases its locks. `onEvent` can inspect the committed session. Callback failures reach `onNotificationError` and do not turn a committed transfer into a failed response. Persist an application outbox for work that must survive a process crash.

An expired finalizing session inspects the destination before cleanup. A committed result is recovered as completed during probe, duplicate creation and expiry sweeping.

import React from "react";
import { createRoot } from "react-dom/client";
import { FileUploader } from "../../packages/file-uploader/src/react";
import { createUploader } from "../../packages/file-uploader/src/core";
import { chunkedTransport } from "../../packages/file-uploader/src/transport/chunked";
const store = createUploader({
  transport: chunkedTransport({ baseURL: "/uploads" }),
  chunkSize: 262144,
});
const App = () => (
  <FileUploader.Root store={store}>
    <FileUploader.Input aria-label="Select files" multiple />
    <FileUploader.Action action="start">Upload</FileUploader.Action>
    <FileUploader.Action
      action="start"
      onClick={(event) => event.preventDefault()}
    >
      Blocked upload
    </FileUploader.Action>
    <FileUploader.Action action="start" disabled>
      Disabled upload
    </FileUploader.Action>
    <button onClick={() => store.setOptions({ disabled: true })}>
      Disable root
    </button>
    <FileUploader.List as="div">
      {(ids) =>
        ids.map((id) => (
          <FileUploader.Item
            as="article"
            id={id}
            key={id}
            className="custom-row"
          >
            <FileUploader.Name />
            <FileUploader.Status />
            <FileUploader.Action action="cancel">Cancel</FileUploader.Action>
            <FileUploader.Action action="retry">Retry</FileUploader.Action>
            <FileUploader.Progress aria-label="Upload progress">
              {(item) =>
                item.progress === null
                  ? "Sending"
                  : Math.round(item.progress) + "%"
              }
            </FileUploader.Progress>
          </FileUploader.Item>
        ))
      }
    </FileUploader.List>
  </FileUploader.Root>
);
createRoot(document.getElementById("app")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);

# File Uploader 0.1.0 implementation

The browser and Node packages are built locally. Registry publication is a separate release operation. The native integrations are source packages in this repository, with their own manifests and build instructions.

## Included

- Single-request HTTP and the verified `salyra-upload/1` chunked transport
- Queue limits, request concurrency, retry and Retry-After, pause, cancel and reset
- IndexedDB session persistence, reselecting the original file and checking every saved chunk before resume
- Per-item subscriptions, coalesced progress updates, byte counts, transfer speed and ETA
- Uploaded records, paginated history and application-owned removal callbacks
- React, Svelte, Vue, Angular, Astro and Vanilla compositions with custom markup, classes, content and native events
- Separate Node server operations, configurable routes, authorization context, session stores and storage adapters
- Filesystem storage and Node S3/R2 storage with a persistent multipart receipt journal
- Native engines for Go, Rust, Java, .NET, Python, PHP, Ruby, Elixir and C, plus Kotlin, Scala and C++ bridges
- API reference, working Preview/Code examples, all six frontend framework recipes, protocol schemas and a versioned documentation snapshot

## Verification

| Check                | Result                                                                                                                                                                                               |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| TypeScript           | Browser engine, adapters, Node server, local examples and test scripts compile                                                                                                                       |
| Frameworks           | Svelte and Astro compile, Angular partial compilation passes, React and Vue SSR isolate stores                                                                                                       |
| Public recipes       | All 54 framework/example combinations parse or compile                                                                                                                                               |
| Unit and fault cases | 22 tests pass, including interrupted responses, retry, persistence, cleanup, provider responses and per-item notification behavior                                                                   |
| Browser interaction  | 12 tests pass against the real local Node server, including five compiled framework adapters, refresh/reselect and completed-file deletion                                                           |
| Documentation        | 23 tests pass across the existing component catalog, uploader guide, browser simulation, mobile layouts, downloads and immutable version links                                                       |
| Package consumer     | Fresh installs of the actual npm archives compile with NodeNext and run without unrequested framework or provider peers. React, Vue and Angular adapter types also compile with their selected peers |
| Native HTTP protocol | Nine running backend engines pass the same compatibility suite                                                                                                                                       |
| Process restart      | Saved parts survive a server restart in all nine backend engines and finish with the same session ID                                                                                                 |
| JVM bridges          | Kotlin and Scala integration projects compile against the Java engine                                                                                                                                |
| C/C++ ownership      | C and C++ tests pass with AddressSanitizer and UndefinedBehaviorSanitizer                                                                                                                            |
| Build contents       | npm archives contain dist, package.json, README and LICENSE. No sourcemaps or source-map references                                                                                                  |

The production documentation build uses `/docs/`, matching GitHub Pages. Its downloadable Vanilla files include standard and minified JavaScript and CSS. Framework JavaScript is minified. Svelte and Astro retain compact compiler inputs, which their toolchains require.

## Deployment boundaries

S3 and R2 behavior is checked with provider response fixtures and the real AWS SDK against a local HTTP endpoint. Live AWS and Cloudflare credentials and buckets have not been used. Built-in provider SDK adapters belong to the Node package. Other languages use their storage interfaces for provider integration.

The included filesystem session stores coordinate on one host, or on connected BEAM nodes for Elixir. Independent hosts need a transactional shared session store. The Python and C filesystem implementations target Unix. Windows-specific filesystem behavior has not been verified.

Each backend uses its own persistence format. The HTTP contract is shared, but a filesystem directory is not a database migration between backend languages.

The native example servers demonstrate routing and streams. Application authentication, access rules, TLS, limits and durable notification delivery belong to the host application. The C socket example is a local compatibility server.

The first distribution does not include a tus adapter or a presigned direct-to-storage transport. These require different reconciliation contracts. The core transport interface remains open to those integrations.

## Reproduce

```sh
npm ci --legacy-peer-deps
npm run check
npm run check:frameworks
npm run check:examples
npm test
npm run build
node scripts/consumer.mjs
node scripts/framework-browser.mjs
npx playwright install chromium
npm run test:browser
npm run test:native -- go
```

Repeat the native command for rust, jvm, python, php, ruby, dotnet, elixir and c. The GitHub workflow runs that matrix and the browser/package checks on pushes and pull requests.

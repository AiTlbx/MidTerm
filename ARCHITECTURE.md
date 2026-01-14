# Architecture

MidTerm is a web-based terminal multiplexer. A native binary serves terminal sessions to browsers via WebSocket, with a binary protocol for efficient I/O multiplexing.

```
┌─────────────────────────────────────────────────────────────┐
│  Browser (xterm.js)                                         │
│  ├─ /ws/mux    Binary multiplexed terminal I/O             │
│  └─ /ws/state  JSON session list updates                   │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  mt (Web Server)                                            │
│  ├─ Kestrel HTTP/WebSocket                                  │
│  ├─ SessionManager                                          │
│  └─ MuxClient per browser connection                        │
└─────────────────────────────────────────────────────────────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
┌───────────────────┐ ┌───────────────────┐ ┌───────────────────┐
│  mthost           │ │  mthost           │ │  mthost           │
│  (PTY session 1)  │ │  (PTY session 2)  │ │  (PTY session N)  │
└───────────────────┘ └───────────────────┘ └───────────────────┘
```

**Binaries:**
- `mt` / `mt.exe` — Web server, session manager, static file host
- `mthost` / `mthost.exe` — PTY host, one per terminal session

## Technology Choices

### Backend: C# Native AOT

The server compiles to a self-contained native binary via .NET's AOT compilation.

**Source-generated serialization**: `AppJsonContext.cs` declares all 48 serializable types at compile time. The runtime never touches reflection. See `Services/AppJsonContext.cs`.

**Platform APIs via P/Invoke**: ConPTY on Windows, forkpty on Unix. Direct system calls, no abstraction layers. See `Ai.Tlbx.MidTerm.TtyHost/Pty/`.

**Single-command deployment**: `dotnet publish -r win-x64 -c Release` produces one executable. No runtime installation, no dependencies.

**Kestrel's async I/O**: The same WebSocket infrastructure that handles production traffic at scale. WebSocket handlers in `Program.cs` lines 300-400.

Binary size: 15-25MB depending on platform. Startup: instant. Memory: stable after initial allocation.

### Frontend: Vanilla TypeScript

The UI is TypeScript without React, Vue, or other frameworks. State management uses nanostores (~1KB), a minimal reactive library that adds computed stores without framework overhead.

**Reactive state**: nanostores in `stores/index.ts` provides atoms, maps, and computed stores for session and UI state.

**Ephemeral state**: `state.ts` holds WebSocket instances, DOM cache, and timers.

**Callback registration**: Modules register functions with each other at startup. `stateChannel.ts` calls `renderSessionList()` without importing the sidebar module. Wiring happens in `main.ts`:

```typescript
registerStateCallbacks({
    destroyTerminalForSession,
    createTerminalForSession,
    renderSessionList,
    selectSession,
    // ...
});
```

**Minimal reactivity**: Computed stores automatically update derived state (e.g., sorted session list). Rendering is still explicit—modules read via `.get()` and call render functions manually. Only one `.subscribe()` call exists (for connection indicator).

**Direct DOM manipulation**: xterm.js requires imperative control. Event handlers attach directly. Elements are created and appended as needed.

This works for terminal UI because:
- The interface has ~15 interactive elements, not hundreds of components
- State changes are server-driven (WebSocket messages), not user interaction cascades
- Terminal output streams at high frequency; virtual DOM diffing would add overhead
- Bundle size matters: the entire TS bundle is smaller than React alone

## Protocols

### Mux Protocol (Binary WebSocket)

Endpoint: `/ws/mux`

All terminal I/O multiplexed over a single WebSocket connection using a binary protocol.

**Frame format:**
```
┌──────────┬────────────────┬─────────────────────────────────┐
│ Type (1) │ SessionId (8)  │ Payload (variable)              │
└──────────┴────────────────┴─────────────────────────────────┘
```

**Message types:**

| Type | Name | Direction | Payload |
|------|------|-----------|---------|
| 0x01 | Output | Server→Client | `[cols:2][rows:2][data...]` |
| 0x02 | Input | Client→Server | Raw bytes |
| 0x03 | Resize | Client→Server | `[cols:2][rows:2]` |
| 0x05 | Resync | Server→Client | Clear all terminals |
| 0x06 | BufferRequest | Client→Server | Request buffer refresh |
| 0x07 | CompressedOutput | Server→Client | `[cols:2][rows:2][uncompLen:4][gzip...]` |
| 0x08 | ActiveSessionHint | Client→Server | Hint for priority |

**Priority buffering** (see `MuxClient.cs`):

The server buffers output differently based on session activity:

- **Active session**: Frames sent immediately. Zero buffering delay.
- **Background sessions**: Batched until 2KB accumulated OR 2 seconds elapsed. Then compressed with GZip and sent as single frame.

This ensures the focused terminal feels responsive while reducing bandwidth for background sessions running builds or logs.

**Resync mechanism**: If the bounded queue overflows (>1000 items), oldest frames are dropped. The server detects this and sends a `Resync` frame, causing clients to clear all terminals and rebuild from current buffers.

### State WebSocket (JSON)

Endpoint: `/ws/state`

Separate channel for sidebar synchronization. Pushes session list whenever sessions change (create, delete, rename, resize).

```json
{
    "sessions": {
        "sessions": [
            { "id": "a1b2c3d4", "name": null, "shellType": "pwsh", "cols": 120, "rows": 30 }
        ]
    },
    "update": { "version": "5.10.0", "isNewer": true }
}
```

Clients use this to render the sidebar without polling. Multiple browser tabs receive updates simultaneously.

### REST API

Authentication, session management, and settings via REST endpoints.

**Authentication flow:**
1. `POST /api/auth/login` with `{ password }` → Sets session cookie
2. Cookie contains `timestamp:hmac-signature` (HMAC-SHA256, 3-week validity)
3. Password stored as PBKDF2 hash (100K iterations, SHA256)

**Session management:**
- `POST /api/sessions` — Create session with optional shell type, working directory
- `DELETE /api/sessions/{id}` — Close session
- `POST /api/sessions/{id}/resize` — Resize terminal dimensions

**Rate limiting:** 5 failures = 30s lockout, 10 failures = 5min lockout.

## Architecture Patterns

### Process Isolation

Each terminal session runs in a separate `mthost` process:

```
mt (web server)
  │
  ├── IPC ──► mthost (session 1) ──► /bin/bash
  ├── IPC ──► mthost (session 2) ──► pwsh.exe
  └── IPC ──► mthost (session 3) ──► /bin/zsh
```

**Benefits:**
- **Crash isolation**: A shell crash kills one mthost, not the server
- **Privilege dropping**: On Unix, mthost can run as non-root user
- **Clean process model**: Each PTY has dedicated file descriptors

**IPC mechanism:**
- Windows: Named pipes (`\\.\pipe\mthost-{sessionId}-{pid}`)
- Unix: Unix domain sockets (`/tmp/mthost-{sessionId}-{pid}.sock`)

Protocol: 5-byte header (type + length) + JSON/binary payload.

### AOT-Safe Patterns

Native AOT compilation prohibits runtime reflection. All dynamic behavior must be resolved at compile time.

**Source-generated JSON** (see `AppJsonContext.cs`):
```csharp
[JsonSerializable(typeof(SessionListDto))]
[JsonSerializable(typeof(CreateSessionRequest))]
// ... 46 more types
internal partial class AppJsonContext : JsonSerializerContext { }
```

**Platform detection**:
```csharp
if (OperatingSystem.IsWindows())
    return new WindowsPty(sessionId, shell, workingDir);
else
    return new UnixPty(sessionId, shell, workingDir);
```

**Compile-time conditionals** for platform-specific code:
```csharp
#if WINDOWS
    [DllImport("kernel32.dll")]
    private static extern bool CreatePseudoConsole(...);
#endif
```

### Frontend State Management

State is split between nanostores (reactive) and module-level variables (ephemeral).

**Nanostores (`stores/index.ts`)** - ~1KB reactive library:
- `atom<T>` for simple values (`$activeSessionId`, `$settingsOpen`)
- `map<Record>` for collections (`$sessions`)
- `computed` for derived state (`$sessionList` auto-sorts when `$sessions` changes)

**Ephemeral state (`state.ts`)** - Non-reactive:
- WebSocket instances, DOM cache, timers, pending buffers

```typescript
// stores/index.ts
export const $sessions = map<Record<string, Session>>({});
export const $activeSessionId = atom<string | null>(null);
export const $sessionList = computed([$sessions], sessions =>
  Object.values(sessions).sort((a, b) => a._order - b._order)
);

// Reading state
const id = $activeSessionId.get();
const session = getSession(id);
```

Modules communicate via callback registration:

```typescript
// stateChannel.ts (WebSocket handler)
function handleStateUpdate(newSessions: Session[]): void {
    setSessions(newSessions);    // Updates $sessions store
    renderSessionList();         // Explicit render call
    updateEmptyState();
}
```

Data flow:
```
WebSocket message
    → handleStateUpdate()
        → setSessions()        // Updates $sessions store
        → $sessionList         // Recomputes automatically
        → renderSessionList()  // Explicit render call
            → DOM mutations
```

Nanostores handles derived state automatically. Explicit render calls remain—no hidden re-renders.

## Design Philosophy

### Simplicity Over Abstraction

The right amount of complexity is the minimum needed for the current task.

- Three similar lines of code > premature abstraction
- Minimal reactivity (computed stores) > full reactive frameworks
- State changes traceable via `.get()`/`.set()` calls
- Direct function calls > message buses

### Explicit Over Implicit

Every state change has a traceable call path.

- Computed stores update derived state automatically
- Rendering remains explicit—no automatic re-renders
- Only one subscription in codebase (connection indicator)
- DOM updates happen when code calls render functions

### Platform-Native Over Cross-Platform Abstraction

Use platform APIs directly rather than abstracting to a lowest common denominator.

- ConPTY on Windows, forkpty on Unix
- Named pipes on Windows, Unix sockets elsewhere
- Compile-time conditionals over runtime factory patterns

## Design Trade-offs

### Minimal State Management

The decision to use nanostores (~1KB) instead of React/Redux has specific implications:

**Debugging**: `grep '\$sessions'` locates store usage. `grep 'setSession'` finds mutations. Call stacks show the path from WebSocket message to store update to explicit render call.

**Coupling**: The callback registration pattern in `main.ts` serves as the dependency graph. Modules don't import each other; they receive function references at startup. This achieves the decoupling that dependency injection provides, without the container.

**Trade-off**: nanostores adds one concept (stores with `.get()`/`.set()`) but keeps the explicit render pattern. New contributors see reactive derived state without the complexity of full frameworks.

### Native AOT vs Runtime Alternatives

C# AOT trades some flexibility for deployment simplicity:

**What works**: P/Invoke to platform APIs (ConPTY, forkpty) compiles cleanly. Source-generated JSON handles all serialization. Kestrel's WebSocket implementation is AOT-compatible.

**Constraints**: No runtime code generation. All types must be known at compile time. The 48-type `AppJsonContext` declares everything upfront.

**Binary size**: 15-25MB includes the runtime, HTTP server, WebSocket handling, and compression. Most of this would exist in any web server; the delta for AOT is minimal.

### Browser-Based Terminal Architecture

Terminal multiplexing in a browser involves inherent constraints:

**Network path**: Every keystroke travels browser → WebSocket → server → PTY → shell. Latency is unavoidable. The binary mux protocol minimizes overhead; priority buffering ensures the active session feels responsive.

**No native terminal emulation**: xterm.js handles escape sequences in JavaScript. It's remarkably complete, but edge cases exist in TUI applications.

**Advantage**: Any device with a browser becomes a terminal client. No SSH configuration. No native app installation. The server handles session persistence across disconnects.

## Testing

The test project (`Ai.Tlbx.MidTerm.Tests/`) provides integration tests for:

- REST API endpoints (sessions, version, resize)
- WebSocket protocols (mux binary frames, state JSON updates)
- Session lifecycle (create, list, delete)

**Current coverage**: Integration tests for core API and WebSocket functionality. Unit tests for isolated services are a focus for upcoming releases.

**Test patterns**:
- `WebApplicationFactory<Program>` for in-process HTTP/WebSocket testing
- `IAsyncLifetime` for test setup/teardown with session cleanup
- Polling helpers for async state verification

## File Reference

| Area | Key Files |
|------|-----------|
| Entry point | `Ai.Tlbx.MidTerm/Program.cs` |
| Mux protocol | `Ai.Tlbx.MidTerm/Services/MuxClient.cs`, `MuxProtocol.cs` |
| Session management | `Ai.Tlbx.MidTerm/Services/TtyHostSessionManager.cs` |
| AOT JSON | `Ai.Tlbx.MidTerm/Services/AppJsonContext.cs` |
| PTY (Windows) | `Ai.Tlbx.MidTerm.TtyHost/Pty/ConPty/` |
| PTY (Unix) | `Ai.Tlbx.MidTerm.TtyHost/Pty/UnixPty.cs` |
| Frontend stores | `Ai.Tlbx.MidTerm/src/ts/stores/index.ts` |
| Ephemeral state | `Ai.Tlbx.MidTerm/src/ts/state.ts` |
| Frontend wiring | `Ai.Tlbx.MidTerm/src/ts/main.ts` |
| Mux client (TS) | `Ai.Tlbx.MidTerm/src/ts/modules/comms/muxChannel.ts` |
| State client (TS) | `Ai.Tlbx.MidTerm/src/ts/modules/comms/stateChannel.ts` |

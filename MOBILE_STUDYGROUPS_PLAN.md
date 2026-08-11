# Mobile — Study Groups Module Plan

> First module to be migrated to .NET MAUI. Students only.
> Shared foundation (auth, HTTP, shell) is in [MOBILE_APP_PLAN.md](MOBILE_APP_PLAN.md) §3, §5, §6.
>
> **Goal: a faithful mirror.** Every rule below already exists and is already correct on the web.
> The mobile client must not re-decide any of them — it calls the API, renders the answer, and
> shows the server's error text when refused.

---

## 1. Surface being migrated

`StudyGroupsController` — 12 actions, all student-facing, all in scope:

| Action | Kind | Notes |
|---|---|---|
| `Index(courseCode?)` | read | Groups for **my enrolled courses only**, my university, non-Archived |
| `Create` GET/POST | write | Course picker comes from the adapter |
| `Details(id)` | read | Info + members + pending requests + chat history |
| `Join(id)` | write | Creates a **Pending** request |
| `ApproveMember(memberId)` | write | Creator only |
| `RejectMember(memberId)` | write | Creator only |
| `RemoveMember(memberId)` | write | Creator only |
| `TransferLeadership(memberId)` | write | Creator only |
| `Leave(id)` | write | Withdraw request, or leave + leadership handover |
| `PostMessage(id, content)` | write | Approved members only — **already returns JSON** |
| `MyCourses()` | read | Enrolled courses via adapter |

No instructor/admin surface exists in this module, so nothing has to be excluded.

---

## 2. Business rules that must survive the port

These are the correctness contract. Each maps to a parity test in §7.

### Create
- Must be **enrolled** in the course (`IUniversityProvider.IsEnrolledAsync`)
- `MinMembers` ≤ `MaxMembers`; both `[Range(2, 50)]`
- **`MaxMembers` ≤ `UniversitySettings.MaxStudyGroupMembers`** (default 10) — a per-university
  ceiling. A student may choose a smaller max, never a larger one. *Easy to miss: it is a database
  lookup, not an attribute.*
- `UniversityCode` is taken from the creator, never from the client
- Creator is auto-added as an **Approved** member

### Join
- Cross-university guard — a group from another university is not joinable
- Must be enrolled in the course
- Rejected if already `Approved` (already in) or `Pending` (already requested)
- Capacity check first: if already full, flips group to `Full` and refuses
- Creates `Pending`, notifies the creator
- **Reactivates** an `Inactive` group to `Active`

### ApproveMember / Reject / Remove / TransferLeadership
- **Creator only** → `Forbid()` otherwise
- Approve requires the membership still be `Pending`
- Capacity re-checked at approval time
- Group flips to `Full` on reaching `MaxMembers`
- **`DbUpdateConcurrencyException` is caught and turned into a retry message** — `StudyGroup` has
  a `[Timestamp] RowVersion`. This is the FR edge case *"simultaneous join requests… manage
  membership count consistently using concurrency control."* The mobile client **must surface that
  retry message**, not swallow it or auto-retry.

### Leave
- A `Pending` request is *withdrawn*; an `Approved` membership is *left* — different messages
- If the **creator** leaves: leadership passes to the **longest-standing approved member**
  (`OrderBy(JoinedAt)`)
- If **nobody** approved remains: group becomes `Archived`
- `Full` → `Active` whenever a seat opens

### PostMessage
- **Approved members only** → `Forbid()`
- Non-empty, trimmed, `[StringLength(1000)]`
- Reactivates an `Inactive` group
- Broadcasts over SignalR **and** persists

---

## 3. Traps — the things that will cause "errors" if not handled

These are the four I would expect to bite, in order of likelihood.

### 3.1 The chat broadcast sends a pre-formatted date string

`StudyGroupsController.PostMessage` broadcasts:

```csharp
await _hub.Clients.Group($"group-{id}").SendAsync("ReceiveMessage", new {
    senderName = user.FullName,
    content    = message.Content,
    sentAt     = message.SentAt.ToString("MMM dd, HH:mm")   // ← a STRING, server-formatted
});
```

That was fine for a browser. On mobile it is a bug waiting to happen: the client cannot re-format
it, cannot localise it, cannot sort by it, and it is culture-dependent on the server.

**Fix: add an ISO field to the broadcast, don't replace the existing one.**

```csharp
sentAt   = message.SentAt.ToString("MMM dd, HH:mm"),  // keep — the web JS reads this
sentAtUtc = message.SentAt                            // add — mobile reads this
```

Additive, so `wwwroot/js/pages/study-group-details.js` keeps working untouched.

### 3.2 `StudyGroup` has a hard FK to the local `Courses` table

Composite FK on `(UniversityCode, CourseCode)`. The adapter can confirm a student is enrolled
while the course row has **not yet been mirrored locally** by `UniversityApiSyncRunner` — the
insert then fails at the database, not at validation.

Already recorded in [test/TEST_PLAN.md](test/TEST_PLAN.md) §9 as a real structural coupling, and it
surfaced only because the test suite uses SQLite; EF's `InMemory` provider would have allowed it
silently.

**The API must return a clean 409/422 with a readable message, not a 500.** The web app has the
same latent issue — this is worth fixing server-side for both clients.

### 3.3 Notifications only arrive while the app is open

`Join` notifies the creator; `Approve` notifies the requester. Both go through
`NotificationService` → `NotificationHub`. With no push in scope, a backgrounded app receives
nothing, and **iOS suspends sockets in the background** regardless.

Not a blocker — but state it as a limitation rather than letting it look like a defect. Fetch
unread notifications on resume so nothing is silently lost.

### 3.4 Chat history is unbounded

`Details` loads every message for the group. Fine for a demo, wrong on mobile.

**Page it: `GET /messages?before={id}&take=30`**, newest first, and load older on scroll.

---

## 4. API endpoints

| # | Method | Route | Wraps |
|---|---|---|---|
| 1 | `GET` | `/api/v1/study-groups?courseCode=` | `Index` |
| 2 | `GET` | `/api/v1/study-groups/{id}` | `Details` |
| 3 | `POST` | `/api/v1/study-groups` | `Create` |
| 4 | `GET` | `/api/v1/study-groups/my-courses` | `MyCourses` — feeds filter + create picker |
| 5 | `POST` | `/api/v1/study-groups/{id}/join` | `Join` |
| 6 | `POST` | `/api/v1/study-groups/{id}/leave` | `Leave` |
| 7 | `POST` | `/api/v1/study-groups/members/{memberId}/approve` | `ApproveMember` |
| 8 | `POST` | `/api/v1/study-groups/members/{memberId}/reject` | `RejectMember` |
| 9 | `POST` | `/api/v1/study-groups/members/{memberId}/remove` | `RemoveMember` |
| 10 | `POST` | `/api/v1/study-groups/members/{memberId}/transfer-leadership` | `TransferLeadership` |
| 11 | `GET` | `/api/v1/study-groups/{id}/messages?before=&take=30` | paged chat history |
| 12 | `POST` | `/api/v1/study-groups/{id}/messages` | `PostMessage` |

**12 endpoints.**

### Response shape

`GET /{id}` should return everything the screen needs in one call, including **the caller's own
state** — otherwise the app has to infer it and will get it wrong:

```jsonc
{
  "id": 12, "groupName": "...", "courseCode": "CSC301", "courseName": "...",
  "status": "Active",              // Active | Full | Archived | Inactive
  "maxMembers": 10, "minMembers": 2, "approvedCount": 4,
  "meetingLocation": "...", "createdAt": "2026-08-10T...",
  "creator": { "userId": "...", "fullName": "..." },
  "myMembership": {                // null if not involved at all
    "memberId": 88, "status": "Pending"   // Pending | Approved | Rejected | Left
  },
  "amCreator": false,
  "canJoin": true, "canPost": false,      // server decides, client renders
  "members":  [ { "memberId": 1, "userId": "...", "fullName": "...", "status": "Approved", "joinedAt": "..." } ],
  "pending":  [ /* only populated when amCreator */ ]
}
```

`canJoin` / `canPost` / `amCreator` are computed **server-side**. The client must never re-derive
permission from member lists — that is how the two clients drift apart.

### Errors

Every refusal the web shows as `TempData["Error"]` becomes a JSON problem response the app
displays verbatim:

```jsonc
{ "error": "This study group is already full.", "code": "GROUP_FULL" }
```

Same strings, same conditions. Add `code` so the app can react (e.g. refresh on `CONCURRENCY_RETRY`)
without parsing English.

### Cross-cutting

- `RequireServiceAttribute` returns a **redirect** today — needs a 403 JSON branch for `/api`.
  Study Groups is service-gated, so this is required, not optional.
- Cookie auth redirects 401s to the login page; `/api` must return a bare 401.

---

## 5. SignalR — reuse the existing hub unchanged

`StudyGroupHub` at `/studygroupHub` already does exactly what the app needs. **No hub changes.**

| Direction | Method | Purpose |
|---|---|---|
| client → server | `JoinGroup(int)` / `LeaveGroup(int)` | Chat room membership |
| client → server | `JoinStudyGroupsLobby()` / `LeaveStudyGroupsLobby()` | List-screen live refresh |
| server → client | `ReceiveMessage({senderName, content, sentAt})` | New chat message (+ `sentAtUtc`, §3.1) |
| server → client | `GroupUpdated` | Re-fetch this group |
| server → client | `StudyGroupListChanged` | Re-fetch the list |

Mobile specifics:
- `.WithAutomaticReconnect()` — mobile networks drop constantly
- Re-join the group/lobby in the `Reconnected` handler; **group membership is not restored automatically**
- Pass the bearer token via `AccessTokenProvider`
- Disconnect on background, reconnect and re-fetch on resume — a socket that survives backgrounding is not something to rely on

`GroupUpdated` and `StudyGroupListChanged` carry **no payload** by design — they are "something
changed, re-fetch" signals. Keep it that way; it is what stops the two clients diverging.

---

## 6. Screens

| # | Screen | Contents |
|---|---|---|
| 1 | **Browse** | Course filter, group cards (name, course, members `4/10`, status pill), lobby live-refresh |
| 2 | **Create** | Name, description, course picker (enrolled only), min/max steppers, meeting location |
| 3 | **Details** | Header + info, member list, "Request to join" / "Leave", **pending requests section when creator** |
| 4 | **Chat** | Paged history, composer, live receive. Tab within Details |
| 5 | **My groups** | Filtered view of Browse — same screen, different query |

**5 screens** (4 distinct, since My groups reuses Browse).

Status pills reuse the web's semantics exactly: `Active` green · `Full` amber · `Inactive` grey ·
`Archived` hidden from the list.

---

## 7. Parity verification — how "without errors" gets proven

Fidelity is a claim; this is the evidence. **Write these as API tests against the existing
xUnit + SQLite harness in `test/UniConnect.Tests`** — the infrastructure is already built.

| # | Scenario | Expected — identical on both clients |
|---|---|---|
| 1 | Browse while enrolled in 2 of 5 courses | Only groups for those 2 courses |
| 2 | Browse a group from another university | Not listed; direct fetch refused |
| 3 | Create with `MaxMembers` above the university ceiling | Rejected with the ceiling message |
| 4 | Create with `Min > Max` | Rejected |
| 5 | Create for a course not enrolled in | Rejected |
| 6 | Creator appears in own group | `Approved`, immediately |
| 7 | Join twice | Second refused, "already have a pending request" |
| 8 | Join a full group | Refused; group flips to `Full` |
| 9 | Approve as non-creator | `403` |
| 10 | Approve the 10th member of a max-10 group | Group becomes `Full` |
| 11 | Two approvals racing on the last seat | One wins; other gets the concurrency retry message — **not** an over-filled group |
| 12 | Creator leaves with members remaining | Leadership → longest-standing by `JoinedAt` |
| 13 | Last member leaves | Group `Archived` |
| 14 | Member leaves a `Full` group | Group returns to `Active` |
| 15 | Post as a `Pending` member | `403` |
| 16 | Post to an `Inactive` group | Message saved; group reactivated |
| 17 | Post empty / 1001 chars | Rejected |
| 18 | Create for a course not yet mirrored locally | Clean 409/422 — **never a 500** (§3.2) |

Test 11 is the one that matters most and is easiest to skip. It is the documented FR edge case,
and it is the reason `RowVersion` exists.

---

## 8. Estimates

Person-days at 8h, solo, AI-assisted per [MOBILE_APP_PLAN.md](MOBILE_APP_PLAN.md) §6.

### One-time foundation (shared with every later module)

| Work | Traditional | **AI-assisted** |
|---|---|---|
| MAUI tooling, real-device deploy, project structure | 3 | **2** |
| DI, `HttpClient`, token handler, `SecureStorage` | 3 | **1.5** |
| Auth API (`MapIdentityApi`) + `/api` 401/403 branches | 3 | **1** |
| Shell navigation + theming | 3 | **1.5** |
| Login screen | 2 | **1** |
| | **14** | **≈ 7** |

### Study Groups module

| Work | Traditional | **AI-assisted** |
|---|---|---|
| 12 API endpoints + DTOs (thin wrappers over existing logic) | 7 | **2.5** |
| Message paging + `sentAtUtc` + FK error handling (§3.1, §3.2, §3.4) | 2 | **1** |
| Browse + Create screens | 4 | **1.5** |
| Details screen (members, pending, join/leave) | 4 | **1.5** |
| Chat screen + paging | 3 | **1.5** |
| SignalR client: reconnect, re-join, background/resume | 4 | **2.5** |
| 18 parity tests (§7) | 4 | **1.5** |
| Device testing + polish | 4 | **3** |
| | **32** | **≈ 15** |

### Total

| | Traditional | **AI-assisted** |
|---|---|---|
| Foundation + Study Groups | 46 | **≈ 22 days** |
| *Study Groups alone, once foundation exists* | 32 | **≈ 15 days** |

**≈ 22 person-days ≈ 4½ weeks at a normal pace, or ~3 weeks full-time.**

SignalR gets the smallest speedup (~1.6x) — reconnection, re-join-on-reconnect and
background/resume are behavioural problems you debug on a physical device, not code you generate.

---

## 9. Order of work

1. **Fix the repo first.** Currently: 7 reverted changes, `UniConnect.sln` references a missing
   `UniConnect.Mobile.csproj`, and the test project does not compile. Do not start on a red tree.
2. **API before app.** All 12 endpoints working in Postman, with the §7 tests green, before opening
   the MAUI templates.
3. **Server-side fixes from §3 while you are in there** — `sentAtUtc`, message paging, the FK error.
   All three also benefit the web app.
4. **Vertical slice:** login → browse → details → join. Ship that before writing the chat screen.
5. **Chat last.** It is the only piece needing SignalR, and it is where the device-specific time goes.

## 10. Rules that keep it a mirror

1. **The server decides; the client renders.** `canJoin`, `canPost`, `amCreator` come from the API.
   No permission logic in the app.
2. **No business rule is re-implemented.** Capacity, ceilings, leadership handover, reactivation —
   all stay in `StudyGroupsController`. The endpoints call the same code.
3. **Error strings come from the server**, so both clients say the same thing.
4. **Additive changes only** to shared code — §3.1 adds `sentAtUtc` rather than changing `sentAt`,
   so the web JS is untouched.
5. **Every rule in §2 has a test in §7.** That is what makes "without errors" checkable rather than
   hopeful.

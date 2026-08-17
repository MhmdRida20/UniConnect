# Two-factor authentication — implementation plan

Web portal only. TOTP (RFC 6238), opt-in per user, with QR enrolment in phase 2.

Written 16 August 2026. **Rechecked line-by-line 17 August 2026** — every file
reference, line number and claim below was re-verified against the working tree
on that date. Corrections from the recheck are marked **[verified 17 Aug]**.

## Status as of 17 August 2026: nothing has been implemented yet

| | |
| --- | --- |
| 2FA pages scaffolded | **None.** `Areas/Identity/Pages/Account/` holds only Login, Register, RegisterChoice, RegisterInstructor, RegisterStaff |
| Local QR library | **None.** `wwwroot/lib` contains no qrcode file |
| `RequiresTwoFactor` branch | **Still missing.** The lockout bug below is live |
| Scaffolder CLI | **Not installed** (see step 2) |
| Test baseline | **271 passed, 0 failed** — re-run 17 Aug, still green |

**The deck already claims 2FA as shipped** (slide 12), and the Q&A notes offer to
demonstrate QR enrolment. Until phase 1 and 2 actually land, that offer cannot be
honoured. See the last section for what to change if they do not.

---

## Read this first: two findings that shape everything below

### 1. There is a latent bug that will lock people out the moment 2FA is enabled

[Areas/Identity/Pages/Account/Login.cshtml.cs:91](../Areas/Identity/Pages/Account/Login.cshtml.cs#L91)
calls `PasswordSignInAsync`, then handles exactly three outcomes:

```csharp
var result = await _signInManager.PasswordSignInAsync(...);

if (result.Succeeded)     { ... redirect ... }      // line 94
if (result.IsLockedOut)   { ... Lockout page ... }  // line 111
ModelState.AddModelError(string.Empty, "Invalid login attempt.");   // line 117
```

`SignInResult` has a **fourth** outcome: `RequiresTwoFactor`. It is not
`Succeeded`, and it is not `IsLockedOut` — so it falls through to line 117.

The consequence: **the first user who turns on 2FA can never log in again.**
They type the correct password and are told "Invalid login attempt." Worse, line
101 classifies the reason as `InvalidCredentials`, so the audit log records a
false failed-login for a user who did nothing wrong.

This is not a risk introduced by the new work — it is already there, waiting.
**Fixing it is step 1 and nothing else may ship before it.**

**[verified 17 Aug] Why `TwoFactorEnabled = 1` alone is enough to trigger it
here.** `SignInManager` returns `RequiresTwoFactor` only when
`IsTwoFactorEnabledAsync` is true, which needs the flag *and* at least one
registered provider that can generate a token. `AddDefaultIdentity`
([Program.cs:32](../Program.cs#L32)) calls `AddDefaultTokenProviders()`
internally — confirmed in practice by
[Register.cshtml.cs:187](../Areas/Identity/Pages/Account/Register.cshtml.cs#L187)
already using `GenerateEmailConfirmationTokenAsync` — so the **Email** provider
is registered, and this app confirms emails. The flag on its own is therefore
sufficient to lock someone out on the current code.

The same fact has a sharp edge worth knowing: a user can end up with
`TwoFactorEnabled = 1` and **no authenticator key**, and `LoginWith2fa` only
offers the authenticator provider. That is a hard lockout recoverable *only*
through the admin reset in step 6 — which is why step 6 is not optional.

### 2. No database migration is required

Both storage locations already exist in the schema:

| What | Where | Confirmed |
| --- | --- | --- |
| `TwoFactorEnabled` flag | `AspNetUsers` | `CreateIdentitySchema.cs:40` |
| Authenticator key + recovery codes | `AspNetUserTokens` | `ApplicationDbContextModelSnapshot.cs:141-159` |

ASP.NET Core Identity has stored these since the initial migration. So this
feature adds **no migration, no schema change, and no data-loss risk** — which
removes the single most dangerous category of failure from the work.

---

## Timing — read before you start

**[verified 17 Aug] It is now Monday 17 August. The defence is tomorrow.** This
section was written assuming a working day plus a buffer; that buffer is gone.
Read the recheck findings before deciding to start — in particular step 8, which
needs roughly half a day of test-harness work that did not exist in the original
estimate. If you begin now, the realistic scope for today is **step 1 alone**:
the `RequiresTwoFactor` fix plus its regression test. That is worth doing on its
own merits — it removes a live lockout bug — but it is not "2FA shipped", and
the deck must be corrected accordingly (last section).

The defence is **Tuesday 18 August**. This is authentication code; a mistake
locks people out of the system you are about to demonstrate.

- Do all of this **on a branch**, never on `main`.
- Keep `main` in its current demo-ready state until phase 1 is fully tested.
- **Do not enable 2FA on the account you will demo with**, on web or mobile.
  See the mobile note in step 7 — a 2FA-enabled account cannot sign in to the
  mobile app.
- If phase 1 is not comfortably green tonight, present from `main` and describe
  2FA as **designed and planned**, matching what the report already says. Do not
  describe it as "completed but unmerged" unless it genuinely is — that phrasing
  was written when there was still a day in hand.

---

## What already exists (do not rebuild any of it)

ASP.NET Core Identity ships the entire TOTP implementation. `AddDefaultIdentity`
in [Program.cs:32](../Program.cs#L32) already registers it, including:

- `GenerateNewAuthenticatorKey()` — the base32 shared secret
- `VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)`
- `SetTwoFactorEnabledAsync`, `GenerateNewTwoFactorRecoveryCodesAsync`
- `TwoFactorAuthenticatorSignInAsync`, `TwoFactorRecoveryCodeSignInAsync`
- The Razor pages themselves, served from the Identity UI package

**You are not writing a TOTP algorithm.** You are wiring up pages that exist,
fixing the login branch, and styling the result. Anything that looks like
implementing HMAC or base32 by hand means you have gone off the path.

Microsoft's reference: *"Enable QR code generation for TOTP authenticator apps
in ASP.NET Core"* — <https://learn.microsoft.com/aspnet/core/security/authentication/identity-enable-qrcodes>

---

# Phase 1 — 2FA working on the web (manual key entry)

At the end of phase 1 a user can enable 2FA by typing the base32 key into their
authenticator app. No QR yet — that is phase 2 deliberately, so that if QR
rendering misbehaves it cannot block a working feature.

### Step 0 — Baseline

```
git checkout -b feature/two-factor
dotnet build
dotnet test test/UniConnect.Tests
```

Record the passing count (**271**, re-confirmed 17 Aug). If it is not 271 before
you start, fix that first — you cannot tell what you broke from a dirty baseline.

**[verified 17 Aug] If the web app is running, the test build fails** with
MSB3021/MSB3027 file-lock errors on `bin\Debug\net8.0\UniConnect.exe` — the
tests never run and it looks like a broken baseline. Either stop the app, or
build elsewhere:

```
dotnet test test/UniConnect.Tests -p:BaseOutputPath=C:\Temp\tbin\
```

**Install the scaffolder now**, before step 2 needs it — see the note there.

### Step 1 — Fix the `RequiresTwoFactor` branch  ← the critical one

**[verified 17 Aug] The insertion point is exact, and getting it wrong is
harmful.** Put the branch **immediately after the closing brace of the
`result.Succeeded` block (line 98), before `FindByEmailAsync` on line 99** —
*not* anywhere lower. The audit-log call sits at lines 101–110, between
`Succeeded` and `IsLockedOut`; a branch placed after it writes a false
`FailedLogin` row for every legitimate 2FA challenge.

```csharp
if (result.Succeeded)
{
    _logger.LogInformation("User logged in.");
    return LocalRedirect(returnUrl);
}

// >>> INSERT HERE — above the audit call, so a 2FA challenge is never
//     recorded as a failed login.
if (result.RequiresTwoFactor)
{
    return RedirectToPage("./LoginWith2fa",
        new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
}

var attemptedUser = await _userManager.FindByEmailAsync(Input.Email);   // line 99
```

Returning above the audit call is preferred over extending the `reason` ternary
on line 101: a 2FA challenge is not a failed login and should not be recorded as
one at all.

**Verify before moving on:** this branch is unreachable until a user has 2FA
enabled, so it is easy to "fix" it wrongly and not notice. Cover it with a test
(step 8) rather than by eye.

### Step 2 — Scaffold the 2FA pages

The pages exist in the Identity UI package but are unstyled and cannot be
audited or modified while they live there.

**[verified 17 Aug] The scaffolder CLI is NOT installed.** `dotnet tool list`
is empty both globally and locally, and there is no `.config/dotnet-tools.json`.
The `Microsoft.VisualStudio.Web.CodeGeneration.Design` **package** is referenced
(8.0.23, `UniConnect.csproj`), but that is not the same thing as the command.
Install it first, matching the project's .NET 8 target:

```
dotnet tool install -g dotnet-aspnet-codegenerator --version 8.0.*
```

**[verified 17 Aug] The command below is one line.** The original used bash `\`
continuations, which are a parse error in PowerShell — your shell.

**[verified 17 Aug] Eight files, not seven.** `Account.Manage.ShowRecoveryCodes`
was missing from the original list. It is the page that displays the codes once,
which is exactly what step 5 tells you to edit, and it is the redirect target of
both `EnableAuthenticator` and `GenerateRecoveryCodes`. Without it you cannot add
the "I have saved these" confirmation.

```
dotnet aspnet-codegenerator identity -dc UniConnect.Data.ApplicationDbContext --files "Account.LoginWith2fa;Account.LoginWithRecoveryCode;Account.Manage.TwoFactorAuthentication;Account.Manage.EnableAuthenticator;Account.Manage.Disable2fa;Account.Manage.GenerateRecoveryCodes;Account.Manage.ResetAuthenticator;Account.Manage.ShowRecoveryCodes"
```

`UniConnect.Data.ApplicationDbContext` is correct — confirmed against
`Data/ApplicationDbContext.cs:5` (namespace) and `:14` (class).

> **DANGER.** The scaffolder overwrites files it generates. `Account.Login` is
> **not** in that list on purpose — your `Login.cshtml` is customised and uses
> `_AuthLayout.cshtml`, and regenerating it would destroy the change from step 1
> along with your styling. Do not add `Account.Login`, `Account.Register`, or any
> `Register*` page to `--files`.
>
> Commit immediately before running the scaffolder, so `git diff` shows exactly
> what it touched and `git checkout --` undoes any surprise.

**[verified 17 Aug] Expect MORE than eight files, and do not panic.** Scaffolding
any `Account.Manage.*` page also emits the Manage shell, none of which exists in
this project yet:

```
Areas/Identity/Pages/Account/Manage/_Layout.cshtml
Areas/Identity/Pages/Account/Manage/_ViewStart.cshtml
Areas/Identity/Pages/Account/Manage/_ViewImports.cshtml
Areas/Identity/Pages/Account/Manage/_ManageNav.cshtml
Areas/Identity/Pages/Account/Manage/_StatusMessage.cshtml
Areas/Identity/Pages/Account/Manage/ManageNavPages.cs
```

That is normal and required. What you are checking `git status` for is that
**`Account/Login.cshtml*` and `Account/Register*` are untouched.** Those are the
files that must not move.

### Step 3 — Point the new pages at your layouts

**[verified 17 Aug] The original premise here was wrong.** The scaffolded pages
do **not** default to a stock Bootstrap layout —
`Areas/Identity/Pages/_ViewStart.cshtml` already sets
`Layout = "/Views/Shared/_Layout.cshtml"`, so they inherit the site layout for
free. Two real problems remain instead:

1. **The generated `Manage/_ViewStart.cshtml` overrides the area one** and points
   at the generated `Manage/_Layout.cshtml`. That generated file is what you fix,
   and it is the one the original plan never mentioned.
2. **Stock Bootstrap *markup*** (`form-floating`, `btn-primary`, `col-md-6`)
   comes with every page and does need replacing.

Then:

- `LoginWith2fa`, `LoginWithRecoveryCode` → `Layout = "/Views/Shared/_AuthLayout.cshtml"`
  (matching `Login.cshtml:6`; the file exists at `Views/Shared/_AuthLayout.cshtml`)
- The `Manage/*` pages → **[verified 17 Aug] this is conditional in your
  codebase, not a single layout.** `Views/Profile/Index.cshtml:6` picks
  `_PortalLayout.cshtml` for Admin / UniversityAdmin / DepartmentStaff / Company
  and otherwise falls through to `_Layout`. Copy that same conditional, or the
  2FA pages will look wrong for every staff role:

  ```csharp
  var isPortalRole = User.IsInRole("Admin") || User.IsInRole("UniversityAdmin")
      || User.IsInRole("DepartmentStaff") || User.IsInRole("Company");
  if (isPortalRole) { Layout = "~/Views/Shared/_PortalLayout.cshtml"; }
  ```

Restyle with the existing `uc-*` classes and the `#i-lock` / `#i-shield` icons
from the sprite. No new CSS vocabulary.

### Step 4 — Carry over the rules the normal login already enforces

The scaffolded `LoginWith2fa` page knows nothing about your application's rules.
Check each of these against `Login.cshtml.cs` and `AuthApiController.cs` and add
what is missing:

- **`IsSuspended`** — **[verified 17 Aug] this is defence in depth, not an open
  hole.** The original wording implied a suspended user could get in through the
  2FA path. They cannot: `Middleware/SuspendedUserMiddleware.cs` checks
  `IsSuspended` on *every* authenticated request and redirects to
  `/Identity/Account/Login?suspended=true`, so a suspended user can complete the
  challenge but is signed out on their very next click. Still add the explicit
  check — failing at the challenge is cleaner than a confusing bounce — but do
  not treat it as a blocker. `AuthApiController.cs:102` is the mobile equivalent.
- **Audit logging** — log the 2FA success and failure the same way `FailedLogin`
  is logged today, so the audit trail does not go quiet for 2FA users.
- **Lockout** — `TwoFactorAuthenticatorSignInAsync` takes a
  `rememberClient` flag and applies lockout on failure. Keep `lockoutOnFailure`
  consistent with the `true` used at `Login.cshtml.cs:92`.

### Step 5 — Recovery codes

Non-negotiable, and the step most often skipped. A student who reinstalls their
phone without recovery codes is locked out permanently.

- On enable, generate 10 codes, show them **once**, and require the user to tick
  "I have saved these" before finishing.
- Show the remaining count on the `TwoFactorAuthentication` page.
- Make `LoginWithRecoveryCode` reachable by a visible link from `LoginWith2fa`,
  not just by URL.

### Step 6 — Admin escape hatch  ← do not skip

Even with recovery codes, someone will lose both. You already have an admin area
managing users; add one action:

> **Reset two-factor for this user** — calls `SetTwoFactorEnabledAsync(user, false)`
> and `ResetAuthenticatorKeyAsync(user)`, and writes an audit entry naming the
> admin who did it.

Without this, your only recovery is editing the database by hand during a demo.
The audit entry is what stops the escape hatch becoming a back door.

### Step 7 — Decide what the mobile app does

**This is the one place where "web only" needs an explicit decision rather than
silence.**

[Controllers/Api/AuthApiController.cs:74-80](../Controllers/Api/AuthApiController.cs#L74)
uses `CheckPasswordSignInAsync`, with a comment explaining it avoids writing a
cookie. That is correct reasoning for an API — but `CheckPasswordSignInAsync`
**validates the password only and ignores `TwoFactorEnabled` entirely**.

So with no change: a user enables 2FA on the web, and the mobile app still logs
them in with just a password. The second factor is bypassed, and the security
claim on the slide is not true.

Three options:

| Option | Effect | Verdict |
| --- | --- | --- |
| **A.** Leave as-is | Mobile silently bypasses 2FA | **No.** It makes the feature cosmetic. |
| **B.** Reject 2FA accounts on the mobile API | `if (user.TwoFactorEnabled) return 403 "use the web portal"`. No mobile release needed — the app shows the error. | **Recommended for now.** Honest, ~3 lines, no bypass. |
| **C.** Accept an optional `totpCode` in the login request | Mobile keeps working, no bypass | The right long-term answer; needs a mobile UI change, so phase 3. |

Take **B**, and say so in the enrolment page: *"While two-factor is on, you can
sign in on the web portal only. Mobile support is coming."* Then the user is
choosing it, not discovering it.

**[verified 17 Aug] The blast radius is narrower than it looks.**
`AuthApiController.cs:30` declares `MobileAllowedRoles = { "Student" }` and
`:110` rejects everything else, so option B can only ever affect student
accounts — instructors, staff, admin and company users are already turned away
from the mobile API regardless of 2FA.

**Consequence for Tuesday:** a 2FA-enabled account cannot sign in on mobile. Keep
the demo account without 2FA.

### Step 8 — Tests

> **[verified 17 Aug] This step was badly under-scoped, and it is the single
> biggest correction in this recheck.** The original read "add 6 tests". The
> existing harness cannot run four of them without new infrastructure. Budget
> roughly **half a day of harness work before the first 2FA test exists.**

Two concrete blockers in `test/UniConnect.Tests/Infrastructure/IdentityHarness.cs`:

**(a) No token providers are registered.** The harness constructs `UserManager`
by hand and passes `new ServiceCollection().BuildServiceProvider()` as the
services argument. Token providers are normally registered by
`AddDefaultTokenProviders()` at DI time, which never runs here — so
`VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)`
throws `NotSupportedException: No IUserTwoFactorTokenProvider<ApplicationUser>
named 'Authenticator' is registered`. One line fixes it:

```csharp
manager.RegisterTokenProvider(
    TokenOptions.DefaultAuthenticatorProvider,
    new AuthenticatorTokenProvider<ApplicationUser>());
```

**(b) There is no `SignInManager` in the harness at all.** Building a real one
needs `IHttpContextAccessor`, `IUserClaimsPrincipalFactory`,
`IAuthenticationSchemeProvider`, `IUserConfirmation`, *and* a working
`IAuthenticationService` inside `HttpContext.RequestServices` — because
`PasswordSignInAsync` reaches `IsTwoFactorClientRememberedAsync`, which calls
`Context.AuthenticateAsync`.

**Do not build that.** `SignInManager`'s methods are virtual: subclass it and
override the one method under test. This also tests *your* fix rather than
re-testing Microsoft's Identity, which is what test 1 was really about.

Revised list, with what each actually costs:

| # | Test | Feasibility |
| --- | --- | --- |
| 1 | **`LoginModel.OnPostAsync` redirects to `LoginWith2fa`** when the sign-in manager returns `TwoFactorRequired` | **Do this one first.** Stub `SignInManager` subclass returning `SignInResult.TwoFactorRequired`; assert a `RedirectToPageResult` with `PageName == "./LoginWith2fa"`. This is the regression test for the step-1 bug. |
| 2 | A valid TOTP code verifies | Cheap **once (a) is done** — `GenerateNewAuthenticatorKey` → `SetAuthenticationTokenAsync` → `GenerateTwoFactorTokenAsync` → `VerifyTwoFactorTokenAsync`. Tests the token, not the sign-in. |
| 3 | A wrong code fails | Same, trivial once 2 works |
| 4 | A used recovery code cannot be reused | **Works with the harness as it stands today** — `GenerateNewTwoFactorRecoveryCodesAsync` / `RedeemTwoFactorRecoveryCodeAsync` need no provider |
| 5 | A suspended user cannot complete a challenge | Reframe per step 4 — assert `SuspendedUserMiddleware` signs them out, which is the mechanism that actually protects you |
| 6 | Mobile API returns 403 for a 2FA account | Feasible — `CheckPasswordSignInAsync` never touches `HttpContext`, so a lightly stubbed `SignInManager` is enough |

> **[verified 17 Aug] Two traps that produce silent, confusing failures:**
>
> - **Test users must have `EmailConfirmed = true`.** `Program.cs:34` sets
>   `RequireConfirmedAccount = true`, so `PasswordSignInAsync` returns
>   `IsNotAllowed` and *never* `RequiresTwoFactor` for an unconfirmed user. Your
>   test will fail for a reason that has nothing to do with 2FA.
> - **`TwoFactorEnabled = true` alone is not enough to make Identity ask for a
>   second factor.** `IsTwoFactorEnabledAsync` also requires at least one
>   registered provider whose `CanGenerateTwoFactorTokenAsync` returns true — so
>   the user needs an authenticator key set, or the test sees `Succeeded`.

For 2 and 3, generate the code through `TokenOptions.DefaultAuthenticatorProvider`
on `UserManager` rather than hand-rolling TOTP, so the test exercises the real path.

### Phase 1 done when

- [ ] 271 + the new tests pass (test 1 is the non-negotiable one)
- [ ] A user can enable 2FA by typing the key, log out, log back in with a code
- [ ] Recovery code login works, and a used code is rejected
- [ ] Admin reset works and is audited
- [ ] A user **without** 2FA logs in exactly as before — regression check
- [ ] The mobile app still logs in normally for non-2FA accounts

---

# Phase 2 — QR enrolment

Only start this when phase 1 is green. The point of the split is that QR is a
convenience on top of a working feature, so a QR problem can never leave 2FA
broken.

### Step 1 — Get the QR library locally

You already render QR client-side for attendance
([wwwroot/js/pages/attendance-details.js:21-33](../wwwroot/js/pages/attendance-details.js#L21)),
using `QRCode.toCanvas` from the `qrcode` library — **loaded from the unpkg CDN**.

**[verified 17 Aug] The exact reference** is
`<script src="https://unpkg.com/qrcode@1.5.0/build/qrcode.js">` at
[Views/InstructorAttendance/Details.cshtml:199](../Views/InstructorAttendance/Details.cshtml#L199).
Note the file is `qrcode.js`, **not** `qrcode.min.js` as the original plan said.
That one tag serves all three `toCanvas` call sites in the JS (lines 30, 53, 76),
so localising attendance too is a single-line change.

For 2FA, download `qrcode.js` (v1.5.0, to match) into `wwwroot/lib/qrcode/` and
reference it locally.
Two reasons, and the second is the important one:

1. A CDN outage during a demo breaks enrolment.
2. The QR encodes the **shared secret**. Keeping the renderer as a local,
   version-pinned file avoids trusting a third-party CDN with the code that
   handles it. (The secret is never sent to the CDN either way — but a
   compromised CDN script could read it from the page.)

Consider doing the same for the attendance page while you are there.

### Step 2 — Build the `otpauth://` URI correctly

This is where implementations usually go wrong. The format:

```
otpauth://totp/{issuer}:{account}?secret={key}&issuer={issuer}&digits=6
```

Rules that matter:

- **URL-encode both the issuer and the account**, in the label *and* the query.
  "University of Sciences and Arts" contains spaces; unencoded, some apps fail
  to parse the URI and others import a mangled name.
- The **issuer must be identical** in the label prefix and the `issuer`
  parameter, or authenticator apps display the entry inconsistently.
- Use the **unformatted** key from `GetAuthenticatorKeyAsync` — not the
  space-separated version you display for manual entry. Spaces in the `secret`
  parameter produce a key that silently fails to validate.
- Keep `digits=6` and the default 30-second period. Do not customise them;
  several authenticator apps ignore non-default values and you get codes that
  never match.

The scaffolded `EnableAuthenticator.cshtml.cs` ships with an
`AuthenticatorUriFormat` constant and a `GenerateQrCodeUri` method that does this
with `UrlEncoder`. **Use it as generated.** Set the issuer to `UniConnect` (or
the university name) and change nothing else about the format string.

### Step 3 — Render it

In `EnableAuthenticator.cshtml`, the scaffolder leaves a `div` with
`data-url="@Model.AuthenticatorUri"` and a comment pointing at the QR docs.
Render into it exactly as `attendance-details.js` does, including its fallback:

> if the library did not load, show the manual key instead of an empty box

That fallback is why phase 1 ships manual entry first — the manual path is the
degraded mode, and it already works.

### Step 4 — Test against real authenticator apps

Emulators are not sufficient. Scan with at least two of: Google Authenticator,
Microsoft Authenticator, Authy. Check:

- The entry name reads sensibly (not URL-encoded gibberish, not "undefined")
- The first generated code is accepted
- A code accepted at the very end of its 30-second window still works
  (Identity allows a small clock skew; confirm rather than assume)

### Step 5 — Server clock

TOTP is time-based. If the server clock drifts more than ~30s, every code fails
and it looks like a broken implementation. Confirm the machine syncs time via
NTP, and note it as a deployment requirement.

### Phase 2 done when

- [ ] Scanning the QR enrols in one step on two different authenticator apps
- [ ] The manual key still works as a fallback
- [ ] With the QR library blocked in devtools, the page degrades to the manual
      key instead of showing an empty box

---

## Guarantee measures — why this should not break the system

| Risk | Why it is contained |
| --- | --- |
| Schema damage / data loss | **No migration.** The columns and tables already exist. |
| Existing users affected | 2FA is **opt-in**. Nobody's login changes until they choose it. |
| The lockout bug | Fixed in step 1, *before* anyone can enable 2FA, and pinned by a regression test. |
| Scaffolder destroying custom pages | Explicit `--files` list excluding `Login`/`Register*`; commit immediately before running it. |
| User loses their phone | Recovery codes (step 5) plus an audited admin reset (step 6). |
| Silent 2FA bypass on mobile | Closed explicitly in step 7, rather than left undecided. |
| Something goes wrong anyway | Whole feature is one branch. `git revert` the merge; no schema to unwind. |
| Breaking the demo | Work on a branch; `main` stays demo-ready; do not enable 2FA on the demo account. |

### Rollback

Because there is no migration, rollback is genuinely clean:

```
git revert -m 1 <merge-commit>
```

Any user who had enabled 2FA keeps a `TwoFactorEnabled = 1` row. **Before
reverting, clear it**, or those users hit the step-1 bug again on the reverted
code:

```sql
UPDATE AspNetUsers SET TwoFactorEnabled = 0 WHERE TwoFactorEnabled = 1;
DELETE FROM AspNetUserTokens WHERE Name IN ('AuthenticatorKey', 'RecoveryCodes');
```

---

## Effect on the report and the deck

The report's Future Work section describes 2FA as planned, and describes exactly
this design — **[verified 17 Aug]** the text reads *"The intended design is
time-based one-time passwords (TOTP, RFC 6238 [12])… the server will render the
standard otpauth:// provisioning URI as a QR code"*. Consistent with this plan.

**[verified 17 Aug] It is slide 12, not slide 9.** The original said 9 in both
places. `slide_security` calls `footer(s, 12)` at
[build_presentation.py:1190](presentation/build_presentation.py#L1190). The bullet
lives in the `enforced` list at line 1159.

If phase 1 and 2 both land, that is consistent and you can say the report
documented the design and it was completed after submission.

**If they do not land, two things must change before Tuesday:**

1. **Slide 12** — remove the `("Two-factor authentication. ", …)` entry from the
   `enforced` list in `slide_security`
   ([build_presentation.py:1159](presentation/build_presentation.py#L1159)),
   drop the matching paragraph from that slide's speaker notes (line ~1206), and
   re-run the build.
2. **The Q&A notes** (line ~1488) currently answer *"Your report lists two-factor
   authentication as future work"* with *"It was finished after the report was
   submitted… Offer to demonstrate the QR enrolment."* **Delete that offer.** An
   examiner who accepts it is the worst possible way for this to surface.

Do not present it as shipped if it is not.

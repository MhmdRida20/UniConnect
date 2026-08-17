# Email confirmation by code instead of link

Replace the "click this link to confirm" email with a 6-digit code the user types.

Written 17 August 2026. Every claim below was **verified by running it**, not
recalled — a probe test was written against the real Identity classes, its
findings recorded here, and then deleted.

---

## What is true today

Four places generate a confirmation token, and **nothing in this project consumes
one**:

| Generates | File |
| --- | --- |
| Student web registration | [Register.cshtml.cs:187](../Areas/Identity/Pages/Account/Register.cshtml.cs#L187) |
| Instructor registration | [RegisterInstructor.cshtml.cs:159](../Areas/Identity/Pages/Account/RegisterInstructor.cshtml.cs#L159) |
| Staff registration | [RegisterStaff.cshtml.cs:163](../Areas/Identity/Pages/Account/RegisterStaff.cshtml.cs#L163) |
| Mobile registration | [AuthApiController.cs:227](../Controllers/Api/AuthApiController.cs#L227) (`SendConfirmationEmailAsync`) |

All four do the same three things: generate a token, Base64Url-encode it, and
build a `Url.Page("/Account/ConfirmEmail", …)` link.

**`ConfirmEmail`, `RegisterConfirmation` and `ResendEmailConfirmation` do not
exist in this repository.** They are served from the
`Microsoft.AspNetCore.Identity.UI` package. That is why no file calls
`ConfirmEmailAsync` — the package page does. They must be scaffolded before they
can be changed, exactly as the 2FA pages were.

`Program.cs:34` sets `RequireConfirmedAccount = true`, so this flow is on the
critical path: an unconfirmed user cannot sign in.

---

## Verified findings

Setting one option changes the token format, and the standard API keeps working:

```csharp
options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultEmailProvider;
```

| Question | Measured answer |
| --- | --- |
| What does `GenerateEmailConfirmationTokenAsync` return? | `"120907"` — **6 characters, all digits** |
| Does `ConfirmEmailAsync(user, code)` still work? | **Yes.** No custom verification needed |
| Is `EmailConfirmed` actually set? | **Yes** |
| Is the code bound to the user? | **Yes** — another user's code is rejected |
| Is it stable if generated twice? | **Yes** — same code within a time window, so "resend" reissues the same one |
| Can the code be used twice? | **Yes** — see the hardening step below |
| How long is it valid? | **6 to 9 minutes** — see below |

### The validity window, measured

`EmailTokenProvider` derives from `TotpSecurityStampBasedTokenProvider`, which
uses `Rfc6238AuthenticationService`. Reproducing that algorithm at candidate
timesteps and comparing against a real generated code:

```
timestep  0.5 min -> 728080
timestep    1 min -> 803012
timestep    3 min -> 135778   <== MATCHES
timestep    5 min -> 502956
real code          = 135778
```

**The timestep is 3 minutes**, and `ValidateCode` scans `i = -2..+2` around the
current step. A code generated in step *G* is accepted while the clock is in
steps *G* through *G+2* — so **at least 6 minutes, at most 9**, depending on
where in the step it was generated.

That is short but usable. It is not configurable: the timestep is a private
static field on Identity's TOTP service, not an option. Anything longer means
writing a custom token provider, which is out of scope here.

**Consequence for the UI:** the code-entry page needs a prominent "Send a new
code" button, and the email must say the code expires in a few minutes.

---

## The one thing that makes this a security *downgrade* if ignored

Today's link carries a Data Protection token — effectively unguessable. A
6-digit code has **one million** possibilities and lives for up to 9 minutes.

`ConfirmEmailAsync` applies **no lockout and no attempt counting**. Without a
limit, an attacker who knows a registered email address can simply try codes.
At a modest 100 requests per second, roughly 54,000 codes can be tried inside a
9-minute window — about a **5% chance of success per window**, repeatable.

**Attempt limiting is therefore mandatory, not a nice-to-have.** It is the only
thing that keeps this change from weakening account verification. Implemented as
step 5 below.

---

# The plan

### Step 0 — Branch and baseline

```
git checkout -b feature/email-code
dotnet test test/UniConnect.Tests -p:BaseOutputPath=C:\Temp\tbin\
```

Expect **290 passed**. If the web app is running, the test build fails with
MSB3021/MSB3027 file locks — hence the separate output path.

### Step 1 — Switch the token provider

In `Program.cs`, inside the existing `AddDefaultIdentity` options block:

```csharp
options.SignIn.RequireConfirmedAccount = true;   // already there
options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultEmailProvider;
```

This affects **only** email confirmation. Password reset uses
`Tokens.PasswordResetTokenProvider`, which is untouched and stays a long
Data Protection token — correct, because a reset link is clicked, not typed.

### Step 2 — Scaffold the three pages

They are in the package and cannot be edited in place:

```
dotnet tool install -g dotnet-aspnet-codegenerator --version 8.0.*
```

```
dotnet aspnet-codegenerator identity -dc UniConnect.Data.ApplicationDbContext --files "Account.ConfirmEmail;Account.RegisterConfirmation;Account.ResendEmailConfirmation"
```

> **Do not add `Account.Login` or any `Register*` page to `--files`.** All four
> registration pages here are heavily customised, and the scaffolder overwrites
> what it generates. Commit immediately before running it.

If the app is running, the scaffolder's build fails on the locked exe. Work
around it exactly as before:

```
BaseOutputPath=C:/Temp/sgbin/ dotnet aspnet-codegenerator identity …
```

Check `git status` afterwards: only those three pages plus possibly
`_ViewImports` should be new. `Login.cshtml` and `Register*` must be untouched.

### Step 3 — Send a code instead of a link, in all four places

The same edit in each of the four files. Replace the encode-and-build-URL block:

```csharp
// was: Base64Url-encode, Url.Page(...), "clicking here"
var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);

await _emailSender.SendEmailAsync(Input.Email, "Your UniConnect confirmation code",
    $"<p>Your confirmation code is:</p>" +
    $"<p style=\"font-size:28px;font-weight:700;letter-spacing:.18em\">{code}</p>" +
    $"<p>Enter it on the confirmation page. It expires in a few minutes — " +
    $"request a new one if it stops working.</p>");
```

**Delete the `WebEncoders.Base64UrlEncode` line.** It exists only because a
Data Protection token is not URL-safe. A 6-digit code must be sent raw; encoding
it would send the user a base64 string to type.

Then remove the now-unused `using Microsoft.AspNetCore.WebUtilities;` and
`System.Text.Encoding` imports where they become dead.

### Step 4 — Rebuild `ConfirmEmail` as a form

The scaffolded page reads `userId` and `code` from the query string and confirms
on GET. It becomes a page with an input:

- `OnGetAsync(string email)` — show the form, pre-filled with the email
- `OnPostAsync()` — look the user up by email, call
  `ConfirmEmailAsync(user, Input.Code)`, and on success redirect to Login with a
  success message

Style it with `_AuthLayout.cshtml` and the `auth-*` classes, matching
`LoginWith2fa.cshtml` — that page is already a 6-digit code entry form and is
the right template to copy, including `inputmode="numeric"` and
`autocomplete="one-time-code"`.

`RegisterConfirmation` should redirect straight to `ConfirmEmail` carrying the
email, so registration lands the user on the code form rather than on a "check
your email" dead end.

### Step 5 — Attempt limiting ← the mandatory one

`ConfirmEmailAsync` counts nothing. Add it around the call:

```csharp
if (await _userManager.IsLockedOutAsync(user))
{
    ModelState.AddModelError(string.Empty, "Too many attempts. Try again later.");
    return Page();
}

var result = await _userManager.ConfirmEmailAsync(user, Input.Code);

if (!result.Succeeded)
{
    await _userManager.AccessFailedAsync(user);   // trips lockout at the configured limit
    ModelState.AddModelError(string.Empty, "That code is not valid. Check it, or request a new one.");
    return Page();
}

await _userManager.ResetAccessFailedCountAsync(user);
```

`AccessFailedAsync` uses the same lockout settings as password login, so five
wrong codes locks the account briefly — reducing a 5%-per-window attack to
nothing. `LockoutEnabled` is already true on new users (Identity's default).

**Do not skip this step.** Without it the change is a genuine downgrade.

### Step 6 — Make the code single-use (hardening)

Confirmed by probe: the same code is still accepted after a successful
confirmation, because `ConfirmEmailAsync` does not touch the security stamp.
Rotating it afterwards invalidates the code immediately:

```csharp
await _userManager.UpdateSecurityStampAsync(user);
```

Safe here — the user is not signed in yet at confirmation time, so nothing is
disrupted.

### Step 7 — Resend

`ResendEmailConfirmation` sends the same code shape as step 3. Because the
provider is time-based, a resend inside the same 3-minute step returns the
*same* code; that is expected and harmless. Word the button "Send a new code"
and the confirmation "We have sent a code to …".

### Step 8 — Decide what mobile does

`AuthApiController.SendConfirmationEmailAsync` currently emails a link that
opens the **web** page. With a code, a student who registers in the app has
nowhere to type it.

| Option | Verdict |
| --- | --- |
| **A.** Leave it — student opens the web portal to confirm | Works, but a poor first impression on a mobile-first signup |
| **B.** Add `POST /api/auth/confirm-email` taking `{ email, code }`, plus a code screen in the app | **Recommended.** Server side is ~25 lines and reuses steps 5 and 6 |

Take **B**, but note it needs a MAUI change and a rebuild — **not the night
before a demo.** The server endpoint can ship first and independently; the app
continues to work unchanged until its screen exists.

### Step 9 — Tests

Add to `test/UniConnect.Tests`. The existing `IdentityHarness` needs the email
provider registered, the same one-liner already used for the authenticator:

```csharp
manager.RegisterTokenProvider(
    TokenOptions.DefaultEmailProvider, new EmailTokenProvider<ApplicationUser>());
```

and `IdentityOptions.Tokens.EmailConfirmationTokenProvider` set to match Program.cs.

1. The generated token is 6 digits — pins the format the email and UI assume
2. `ConfirmEmailAsync` accepts it and sets `EmailConfirmed`
3. A wrong code is rejected **and increments `AccessFailedCount`**
4. Five wrong codes lock the account
5. Another user's code is rejected
6. After confirmation the code no longer works (step 6)

### Step 10 — End-to-end

Extend `test/smoke/two_factor_e2e.py`, or add a sibling, driving a running
server: register → read the code from the log (SMTP is unconfigured, so
`SmtpEmailSender` writes the message to the console) → post it → sign in.

That is how the two 500-level bugs in the 2FA work were caught; the unit suite
cannot see a page that throws only when requested.

---

## Done when

- [ ] 290 + the new tests pass
- [ ] Registering shows a code form; the emailed 6-digit code confirms the account
- [ ] A wrong code is refused, and repeated wrong codes lock out
- [ ] Resend works and the new code confirms
- [ ] Password reset still works — it must still be a **link**, not a code
- [ ] An already-confirmed account is not broken by visiting the page again

## Rollback

No migration and no schema change — the only persistent effect is
`AspNetUsers.EmailConfirmed`, which this flow already sets today.

`git revert -m 1 <merge-commit>`. Any user mid-registration simply requests a
new link after the revert. Nothing to unwind in the database.

## Risks, and why they are contained

| Risk | Containment |
| --- | --- |
| Brute force of a 6-digit code | Step 5, mandatory, using the existing lockout settings |
| Code expires before the user types it | 6–9 minutes measured, with a prominent resend; email states it expires |
| Scaffolder overwriting the custom registration pages | Explicit `--files` excluding `Login`/`Register*`; commit first |
| Password reset accidentally becomes a code | Only `EmailConfirmationTokenProvider` is changed; reset uses a separate option |
| Mobile registration left stranded | Step 8 decided explicitly rather than by omission |
| Breaking the demo | Branch; `main` stays as-is until the suite is green |

# End-to-end smoke tests

Tests that drive a **running** server over HTTP, rather than calling into the
code the way `test/UniConnect.Tests` does.

They exist because the xUnit suite cannot see a whole class of failure. Razor
compiles at build time, but an `asp-page` pointing at a page that does not
exist, a `_ViewStart` resolving to the wrong layout, or a scaffolded handler
that throws instead of redirecting all produce a **500 at runtime and a clean
build**. Every one of those was present in this feature at some point; two were
found by this script after the unit tests were already green.

## two_factor_e2e.py

Walks the two-factor loop exactly as a user does, and computes the TOTP itself
so the code is genuinely independent of the server:

1. Password sign-in
2. All five Manage pages render (200, not a redirect to Login)
3. The `otpauth://` URI carries the right issuer, the unformatted secret, and
   `digits=6`
4. Enrolment accepts an independently computed code, and shows 10 recovery codes
5. **Signing in again lands on the challenge** — the regression that matters,
   because before the fix a 2FA user was told "Invalid login attempt" forever
6. The challenge completes with a fresh code
7. A wrong code is refused
8. A recovery code signs in, and cannot be used a second time
9. **Cleanup** — two-factor is turned back off

Step 9 is not optional. The script runs against the real development database,
and an account left with two-factor enabled cannot sign in on the mobile app.

### Running it

```
dotnet run --no-launch-profile          # in one terminal
python test/smoke/two_factor_e2e.py     # in another
```

It expects the server on `http://127.0.0.1:5199` — set `ASPNETCORE_URLS` to
match, or edit `BASE` at the top. The account it uses is set by `EMAIL` /
`PASSWORD`; the seeded student passwords have drifted from `DbSeeder`, so it
currently uses a seeded instructor account.

Exit code is 0 only if every check passes.

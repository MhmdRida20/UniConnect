"""
End-to-end two-factor test against the instance running on :5199.

Walks the loop the way a student does: password sign-in, open the 2FA pages
(where a bad asp-page link or a wrong layout would throw), enrol using a code
computed here from the shared key, then sign out and sign back in with a fresh
code. The TOTP is computed independently of the server and of the C# tests, so
agreement between all three is evidence rather than a shared bug.
"""
import base64, hashlib, hmac, http.cookiejar, re, struct, sys, time
import urllib.parse, urllib.request

BASE  = "http://127.0.0.1:5199"

# The seeded student passwords were changed at some point during development,
# so this uses an account whose seeded credentials still work. It runs against
# the real demo database, which makes the cleanup at the end mandatory: an
# account left with 2FA on would be unable to sign in on the mobile app.
EMAIL = "instructor.chami@uni.edu"
PASSWORD = "Instructor@12345"

passed, failed = [], []


def ok(msg):
    print(f"  PASS  {msg}")
    passed.append(msg)


def bad(msg):
    print(f"  FAIL  {msg}")
    failed.append(msg)


def new_session():
    jar = http.cookiejar.CookieJar()
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def get(op, path):
    with op.open(BASE + path, timeout=30) as r:
        return r.status, r.read().decode("utf-8", "replace"), r.geturl()


def post(op, path, fields):
    data = urllib.parse.urlencode(fields).encode()
    with op.open(BASE + path, data=data, timeout=30) as r:
        return r.status, r.read().decode("utf-8", "replace"), r.geturl()


def token(html):
    m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html)
    return m.group(1) if m else None


def totp(base32_key, offset=0):
    key = base32_key.replace(" ", "").upper()
    key += "=" * (-len(key) % 8)
    secret = base64.b32decode(key)
    step = int(time.time()) // 30 + offset
    digest = hmac.new(secret, struct.pack(">Q", step), hashlib.sha1).digest()
    o = digest[-1] & 0x0F
    binary = struct.unpack(">I", digest[o:o + 4])[0] & 0x7FFFFFFF
    return f"{binary % 1_000_000:06d}"


def sign_in_password(op):
    _, html, _ = get(op, "/Identity/Account/Login")
    return post(op, "/Identity/Account/Login", {
        "Input.Email": EMAIL,
        "Input.Password": PASSWORD,
        "Input.RememberMe": "false",
        "__RequestVerificationToken": token(html),
    })


# ---------------------------------------------------------------- 1. password
print("== 1. password sign-in ==")
op = new_session()
status, html, url = sign_in_password(op)
if "Invalid login attempt" in html:
    bad("password sign-in rejected - wrong seeded password?")
    sys.exit(1)
ok("password sign-in")

# ------------------------------------------------------------- 2. pages render
print("== 2. the Manage pages render ==")
for page in ("TwoFactorAuthentication", "EnableAuthenticator", "Disable2fa",
             "GenerateRecoveryCodes", "ResetAuthenticator"):
    st, body, _ = get(op, f"/Identity/Account/Manage/{page}")
    ok(f"{page} renders") if st == 200 else bad(f"{page} returned {st}")

st, body, _ = get(op, "/Profile")
if st == 200 and "Two-factor authentication" in body:
    ok("Profile shows the 2FA card")
else:
    bad(f"Profile returned {st} / card missing")

# ------------------------------------------------------------- 3. the otpauth URI
print("== 3. the shared key and otpauth URI ==")
_, enable, _ = get(op, "/Identity/Account/Manage/EnableAuthenticator")

m = re.search(r'<code id="sharedKey"[^>]*>([^<]+)</code>', enable)
if not m:
    bad("no shared key on the enrolment page")
    sys.exit(1)
key = m.group(1).strip()
ok(f"shared key present ({len(key.replace(' ', ''))} chars)")

m = re.search(r'id="qrCodeData" data-url="([^"]+)"', enable)
uri = m.group(1).replace("&amp;", "&") if m else ""
ok("otpauth URI present") if uri else bad("no otpauth URI")

if uri.startswith("otpauth://totp/UniConnect:"):
    ok("issuer prefix is UniConnect, not the scaffolder default")
else:
    bad(f"wrong issuer prefix: {uri[:60]}")

if "issuer=UniConnect" in uri:
    ok("issuer repeated in the query string")
else:
    bad("issuer query parameter missing")

if f"secret={key.replace(' ', '').upper()}" in uri:
    ok("secret is the unformatted key (spaces would break it)")
else:
    bad("secret does not match the displayed key")

if "digits=6" in uri:
    ok("digits=6 left at the interoperable default")
else:
    bad("digits parameter changed")

# ------------------------------------------------------------------ 4. enrol
print("== 4. enrol with a computed code ==")
code = totp(key)
print(f"        computed: {code}")
st, after, url = post(op, "/Identity/Account/Manage/EnableAuthenticator", {
    "Input.Code": code,
    "__RequestVerificationToken": token(enable),
})

if "Verification code is invalid" in after:
    bad("enrolment rejected an independently computed code")
    sys.exit(1)
ok("enrolment accepted the code")

if "Save your recovery codes" in after:
    ok("landed on the recovery codes page")
else:
    bad("did not land on recovery codes")

codes = re.findall(r'<code style="display:block[^>]*>([^<]+)</code>', after)
if len(codes) == 10:
    ok(f"10 recovery codes shown (first: {codes[0].strip()})")
else:
    bad(f"expected 10 recovery codes, got {len(codes)}")

_, status_page, _ = get(op, "/Identity/Account/Manage/TwoFactorAuthentication")
if "Two-factor authentication is on" in status_page:
    ok("status page reports two-factor is on")
else:
    bad("status page does not report it is on")

# -------------------------------------------------- 5. the lockout-bug regression
print("== 5. sign out, sign in again - the regression that mattered ==")
op2 = new_session()
st, after_login, url = sign_in_password(op2)

if "Invalid login attempt" in after_login:
    bad("THE LOCKOUT BUG IS BACK: a 2FA user was told 'invalid login'")
elif "Verify it" in after_login or "LoginWith2fa" in url:
    ok("password sign-in now lands on the two-factor challenge")
else:
    bad(f"unexpected page after password sign-in: {url}")

# ---------------------------------------------------------- 6. complete challenge
print("== 6. complete the challenge ==")
code2 = totp(key)
print(f"        computed: {code2}")
st, after_2fa, url = post(op2, "/Identity/Account/LoginWith2fa?rememberMe=False", {
    "Input.TwoFactorCode": code2,
    "Input.RememberMachine": "false",
    "__RequestVerificationToken": token(after_login),
})

if "Invalid authenticator code" in after_2fa:
    bad("the challenge rejected a valid code")
else:
    st, prof, _ = get(op2, "/Profile")
    ok("signed in with the second factor") if st == 200 else bad(f"not signed in ({st})")

# ------------------------------------------------------- 7. wrong code is refused
print("== 7. a wrong code is refused ==")
op3 = new_session()
_, login3, _ = sign_in_password(op3)
_, wrong, _ = post(op3, "/Identity/Account/LoginWith2fa?rememberMe=False", {
    "Input.TwoFactorCode": "000000",
    "Input.RememberMachine": "false",
    "__RequestVerificationToken": token(login3),
})
if "Invalid authenticator code" in wrong:
    ok("a wrong code is refused")
else:
    bad("a wrong code was NOT refused")

# ------------------------------------------------------------ 8. recovery codes
print("== 8. recovery code, and single use ==")
op4 = new_session()
_, login4, _ = sign_in_password(op4)
_, rc_page, _ = get(op4, "/Identity/Account/LoginWithRecoveryCode")

first = codes[0].strip() if codes else None
if first:
    _, after_rc, _ = post(op4, "/Identity/Account/LoginWithRecoveryCode", {
        "Input.RecoveryCode": first,
        "__RequestVerificationToken": token(rc_page),
    })
    st, prof, _ = get(op4, "/Profile")
    ok("recovery code signed in") if st == 200 else bad(f"recovery code failed ({st})")

    # the same code must not work a second time
    op5 = new_session()
    _, login5, _ = sign_in_password(op5)
    _, rc_page5, _ = get(op5, "/Identity/Account/LoginWithRecoveryCode")
    _, reuse, _ = post(op5, "/Identity/Account/LoginWithRecoveryCode", {
        "Input.RecoveryCode": first,
        "__RequestVerificationToken": token(rc_page5),
    })
    if "Invalid recovery code" in reuse:
        ok("the same recovery code cannot be used twice")
    else:
        bad("a used recovery code was accepted again")

# ------------------------------------------------------------------ 9. cleanup
# Not optional. This ran against the real demo database, and an account left
# with two-factor enabled cannot sign in on the mobile app - exactly the state
# the plan warns against carrying into a demo.
print("== 9. cleanup: turn two-factor back off ==")
op6 = new_session()
_, login6, _ = sign_in_password(op6)
if "Verify it" in login6:
    _, chal, _ = post(op6, "/Identity/Account/LoginWith2fa?rememberMe=False", {
        "Input.TwoFactorCode": totp(key),
        "Input.RememberMachine": "false",
        "__RequestVerificationToken": token(login6),
    })

_, dis, _ = get(op6, "/Identity/Account/Manage/Disable2fa")
post(op6, "/Identity/Account/Manage/Disable2fa", {"__RequestVerificationToken": token(dis)})

_, res, _ = get(op6, "/Identity/Account/Manage/ResetAuthenticator")
post(op6, "/Identity/Account/Manage/ResetAuthenticator", {"__RequestVerificationToken": token(res)})

_, final, _ = get(op6, "/Identity/Account/Manage/TwoFactorAuthentication")
if "Two-factor authentication is off" in final:
    ok("account restored: two-factor is off again")
else:
    bad("CLEANUP FAILED - the account still has two-factor on")

print()
print(f"================  {len(passed)} passed, {len(failed)} failed  ================")
for f in failed:
    print("   FAILED:", f)
sys.exit(1 if failed else 0)

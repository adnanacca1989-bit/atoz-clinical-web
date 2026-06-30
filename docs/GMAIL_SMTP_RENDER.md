# Gmail SMTP on Render — Complete Setup Guide

This guide walks you through enabling **real email delivery** for OTP verification codes in **A to Z Clinical** on Render.

> **Note:** This app is ASP.NET Core. Email is sent via **MailKit** (the .NET equivalent of Node.js `nodemailer`). Environment variables use the same `SMTP_*` names.

---

## How it works

| State | What happens | What users see |
|--------|----------------|----------------|
| SMTP **not** configured | OTP logged as `OTP LOG DELIVERY: 1234` | "Verification code is available in server logs (development mode)" |
| SMTP **configured** | OTP emailed via Gmail | "Check your email for the verification code" |

Check status anytime: **https://atoz-clinical.onrender.com/health**  
Look for: `"emailConfigured": true`

---

## Part 1 — Google / Gmail setup (beginner)

### Step 1: Choose your Gmail account

Use a Gmail you control, for example: `yourname@gmail.com`

You will use this address for:
- `SMTP_USER`
- `SMTP_FROM`

### Step 2: Enable 2-Step Verification

Gmail **requires** 2-Step Verification before you can create App Passwords.

1. Open https://myaccount.google.com/security
2. Click **2-Step Verification**
3. Follow the prompts (phone verification)
4. Finish until it shows **On**

### Step 3: Create a Google App Password

You **cannot** use your normal Gmail password for SMTP.

1. Open https://myaccount.google.com/apppasswords  
   (If missing: Security → 2-Step Verification → App passwords)
2. App name: `AtoZ Clinical Render`
3. Click **Create**
4. Copy the **16-character password** (example: `abcd efgh ijkl mnop`)
5. Save it somewhere safe — Google shows it only once

When pasting into Render, you may remove spaces: `abcdefghijklmnop`

---

## Part 2 — Environment variables (exact values)

Add these to your **Web Service** on Render (not the database):

| Variable | Value | Example |
|----------|--------|---------|
| `SMTP_HOST` | `smtp.gmail.com` | `smtp.gmail.com` |
| `SMTP_PORT` | `587` | `587` |
| `SMTP_USER` | Your full Gmail address | `yourname@gmail.com` |
| `SMTP_PASS` | Google **App Password** (16 chars) | `abcdefghijklmnop` |
| `SMTP_FROM` | Same as `SMTP_USER` for Gmail | `yourname@gmail.com` |

**Never use placeholder text** like `your-email@gmail.com` or `your-app-password`.

---

## Part 3 — Add variables in Render dashboard

### Step 1: Log in to Render

1. Go to https://dashboard.render.com
2. Sign in

### Step 2: Open the correct service

1. Click your **Web Service** (runs the app, e.g. `atoz-clinical`)
2. **Do not** open the PostgreSQL database service

### Step 3: Open Environment

1. Left sidebar → **Environment**
2. Click **Add Environment Variable**

### Step 4: Add each variable

Add all five, one at a time:

```
SMTP_HOST     = smtp.gmail.com
SMTP_PORT     = 587
SMTP_USER     = yourname@gmail.com
SMTP_PASS     = your-16-char-app-password
SMTP_FROM     = yourname@gmail.com
```

### Step 5: Save

1. Click **Save Changes**
2. When prompted, choose **Save and deploy** (or redeploy manually below)

---

## Part 4 — Redeploy

Environment variables load only when the app **starts**.

1. Web Service → **Manual Deploy**
2. **Deploy latest commit**
3. Wait until status is **Live** (2–5 minutes)

---

## Part 5 — Verify it works

### Check 1: Health endpoint

Open in browser:

```
https://atoz-clinical.onrender.com/health
```

**Before SMTP:**
```json
"emailConfigured": false
```

**After SMTP:**
```json
"emailConfigured": true
```

### Check 2: Simple email health

```
https://atoz-clinical.onrender.com/health/email
```

Returns:
```json
{ "emailConfigured": true }
```

### Check 3: Variable presence (no secrets exposed)

```
https://atoz-clinical.onrender.com/debug-email-config
```

All should be `true`:
```json
{
  "SMTP_HOST": true,
  "SMTP_PORT": true,
  "SMTP_USER": true,
  "SMTP_PASS": true,
  "SMTP_FROM": true
}
```

### Check 4: Render startup logs

In Render → **Logs**, search for:

```
SMTP startup check — emailConfigured=True
```

### Check 5: End-to-end test

1. Go to **Register Trial**
2. Enter your real Gmail address
3. Submit registration
4. Check inbox and **spam/junk**
5. Enter the 4-digit code

If email fails, search logs for:
- `Email send failed`
- `Gmail SMTP authentication failed`
- `OTP LOG DELIVERY:` (means SMTP still not active)

---

## Part 6 — Common mistakes and fixes

| Mistake | Symptom | Fix |
|---------|---------|-----|
| Placeholder values in Render | `emailConfigured: false` | Use real Gmail + App Password |
| Normal Gmail password in `SMTP_PASS` | Auth failed in logs | Create App Password |
| 2FA not enabled | Can't create App Password | Enable 2-Step Verification first |
| Vars on **database** service | App still missing SMTP | Add to **Web Service** only |
| Only `SMTP_PORT` set | Other vars false in debug | Set all **5** variables |
| `SMTP_FROM` ≠ `SMTP_USER` | Gmail may reject | Use same Gmail for both |
| No redeploy after saving | Old config still running | Manual Deploy |
| App Password with spaces | Auth fails | Paste without spaces |
| Wrong inbox | "No email" | Use registration email; check spam |
| `/health` still `false` | Codes only in logs | Fix SMTP first; search `OTP LOG DELIVERY:` in logs until then |

---

## Part 7 — Until email works (development mode)

1. Render → Web Service → **Logs**
2. Search: `OTP LOG DELIVERY:`
3. Example log line: `OTP LOG DELIVERY: 4821`
4. Enter that 4-digit code on the verification page

---

## Quick checklist

- [ ] 2-Step Verification enabled on Google account
- [ ] App Password created
- [ ] All 5 `SMTP_*` variables on **Web Service**
- [ ] `SMTP_USER` and `SMTP_FROM` are the same Gmail
- [ ] `SMTP_PASS` is App Password (not login password)
- [ ] Saved and redeployed
- [ ] `/health` shows `"emailConfigured": true`
- [ ] Trial registration sends email to inbox

---

## Support endpoints summary

| URL | Purpose |
|-----|---------|
| `/health` | Full status including `emailConfigured` |
| `/health/email` | `{ "emailConfigured": true/false }` only |
| `/debug-email-config` | Which SMTP vars are set (no values) |

namespace AtoZClinical.Web.Middleware;

/// <summary>Self-contained login page that never uses data protection or antiforgery.</summary>
public static class LoginFallbackHtml
{
    public static string Render(bool showSessionResetMessage)
    {
        var notice = showSessionResetMessage
            ? """
              <div style="background:#fff3cd;border:1px solid #ffc107;border-radius:6px;padding:0.75rem;margin-bottom:1rem;font-size:0.9rem;">
                Your browser session was reset. Please sign in again.
              </div>
              """
            : string.Empty;

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <meta http-equiv="Cache-Control" content="no-store, no-cache, must-revalidate" />
                <title>Login - A to Z Clinical</title>
                <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css" />
                <link rel="stylesheet" href="/css/clinical.css" />
            </head>
            <body>
                <div class="login-panel atz-card p-4">
                    <div class="text-center mb-4">
                        <h3 class="fw-bold" style="color:#0b4f8a">A to Z Clinical</h3>
                        <p class="text-muted mb-0">Sign in to continue</p>
                    </div>
                    {{notice}}
                    <div id="clientError" class="alert alert-danger py-2 d-none" role="alert"></div>
                    <form method="post" action="/Account/Login" id="loginForm" autocomplete="on" novalidate>
                        <div class="mb-3">
                            <label class="form-label" for="username">Username</label>
                            <input id="username" name="Input.Username" class="form-control" autocomplete="username" />
                        </div>
                        <div class="mb-3">
                            <label class="form-label" for="password">Password</label>
                            <input id="password" name="Input.Password" type="password" class="form-control" autocomplete="current-password" />
                        </div>
                        <div class="form-check mb-3">
                            <input type="hidden" name="Input.RememberMe" value="false" />
                            <input id="rememberMe" name="Input.RememberMe" type="checkbox" class="form-check-input" value="true" />
                            <label class="form-check-label" for="rememberMe">Remember me</label>
                        </div>
                        <button type="submit" id="loginSubmitBtn" class="btn btn-atz-primary w-100">Login</button>
                    </form>
                    <div class="text-center mt-3 d-flex flex-column gap-1">
                        <a href="/Account/ForgotPassword">Forgot password?</a>
                        <a href="/Portal/Login">Patient portal</a>
                        <a href="/">Back to home</a>
                    </div>
                </div>
                <script>
                (function () {
                    var form = document.getElementById('loginForm');
                    var username = document.getElementById('username');
                    var password = document.getElementById('password');
                    var submitBtn = document.getElementById('loginSubmitBtn');
                    var clientError = document.getElementById('clientError');
                    if (!form || !username || !password || !submitBtn) return;
                    form.addEventListener('submit', function (e) {
                        if (clientError) { clientError.textContent = ''; clientError.classList.add('d-none'); }
                        var u = (username.value || '').trim();
                        var p = password.value || '';
                        if (!u || !p) {
                            e.preventDefault();
                            if (clientError) {
                                clientError.textContent = 'Please enter your username and password.';
                                clientError.classList.remove('d-none');
                            }
                            (u ? password : username).focus();
                            return;
                        }
                        if (form.dataset.submitting === '1') {
                            e.preventDefault();
                            return;
                        }
                        form.dataset.submitting = '1';
                        submitBtn.textContent = 'Signing in…';
                        submitBtn.setAttribute('aria-busy', 'true');
                    });
                })();
                </script>
            </body>
            </html>
            """;
    }
}

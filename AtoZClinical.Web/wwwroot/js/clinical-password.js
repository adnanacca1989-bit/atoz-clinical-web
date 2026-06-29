/** Toggle password field visibility (show/hide eye button). */
function initClinicalPasswordToggle(inputId, toggleId) {
    const input = document.getElementById(inputId);
    const toggle = document.getElementById(toggleId);
    if (!input || !toggle) return;

    const setHidden = () => {
        input.type = 'password';
        toggle.textContent = '👁';
        toggle.setAttribute('aria-label', 'Show password');
        toggle.title = 'Show password';
        toggle.classList.remove('is-visible');
    };

    const setVisible = () => {
        input.type = 'text';
        toggle.textContent = '🙈';
        toggle.setAttribute('aria-label', 'Hide password');
        toggle.title = 'Hide password';
        toggle.classList.add('is-visible');
    };

    toggle.addEventListener('click', () => {
        if (input.type === 'password')
            setVisible();
        else
            setHidden();
    });

    setHidden();
}

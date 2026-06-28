document.addEventListener('DOMContentLoaded', () => {
    const vendorSelect = document.getElementById('cashPaymentVendorSelect');
    if (!vendorSelect) return;

    const patientFields = document.querySelectorAll('.patient-payment-fields');
    const patientNameInput = document.getElementById('cashPaymentPatientNameInput');
    const paymentMethodSelect = document.getElementById('cashPaymentMethodSelect');
    const accountSelect = document.getElementById('cashPaymentAccountSelect');
    const openPatientBtn = document.getElementById('openPatientSelectBtn');

    const patientMethods = ['Cash', 'Card', 'Bank Transfer', 'Cheque', 'Health Insurance Co'];
    const vendorMethods = ['Cash', 'Bank Transfer', 'Credit'];

    const toggleVendorMode = () => {
        const isVendor = !!vendorSelect.value;
        patientFields.forEach(el => el.classList.toggle('d-none', isVendor));
        if (openPatientBtn) openPatientBtn.style.display = isVendor ? 'none' : '';

        if (isVendor) {
            const selected = vendorSelect.options[vendorSelect.selectedIndex];
            if (patientNameInput) patientNameInput.value = selected?.text?.trim() || '';
            rebuildPaymentMethods(vendorMethods);
            rebuildAccountOptions(window.cashPaymentExpenseAccounts || []);
        } else {
            rebuildPaymentMethods(patientMethods);
            rebuildAccountOptions(window.cashPaymentAllAccounts || [], true);
        }
    };

    const rebuildPaymentMethods = (methods) => {
        if (!paymentMethodSelect) return;
        const current = paymentMethodSelect.value;
        paymentMethodSelect.innerHTML = methods
            .map(m => `<option value="${m}"${m === current ? ' selected' : ''}>${m}</option>`)
            .join('');
        if (!methods.includes(current) && methods.length) {
            paymentMethodSelect.value = methods[0];
        }
    };

    const rebuildAccountOptions = (accounts, grouped = false) => {
        if (!accountSelect) return;
        const current = accountSelect.value;
        let html = '<option value="">-- Select Account --</option>';

        if (grouped) {
            const byCategory = {};
            accounts.forEach(a => {
                byCategory[a.categoryType] = byCategory[a.categoryType] || [];
                byCategory[a.categoryType].push(a);
            });
            Object.keys(byCategory).sort().forEach(cat => {
                html += `<optgroup label="${cat}">`;
                byCategory[cat].sort((x, y) => x.accountNo - y.accountNo).forEach(a => {
                    html += `<option value="${a.name}"${a.name === current ? ' selected' : ''}>${a.accountNo} — ${a.name}</option>`;
                });
                html += '</optgroup>';
            });
        } else {
            accounts.sort((x, y) => x.accountNo - y.accountNo).forEach(a => {
                html += `<option value="${a.name}"${a.name === current ? ' selected' : ''}>${a.accountNo} — ${a.name}</option>`;
            });
        }

        accountSelect.innerHTML = html;
    };

    vendorSelect.addEventListener('change', toggleVendorMode);
});

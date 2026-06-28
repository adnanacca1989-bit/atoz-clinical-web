/** Load patient outstanding balance into cash receipt / payment forms. */
async function loadPatientCashBalance(patient) {
    const balanceDueEl = document.querySelector('[name="Input.BalanceDue"]');
    const balanceStatusEl = document.querySelector('[name="Input.BalanceStatus"]');
    if (!balanceDueEl || !patient) return;

    const params = new URLSearchParams();
    if (patient.patientNo) params.set('patientBarcode', patient.patientNo);
    if (patient.name) params.set('patientName', patient.name);
    if (patient.id) params.set('patientRecordId', patient.id);
    const doctor = patient.doctorName || document.querySelector('[name="Input.DoctorName"]')?.value?.trim();
    if (doctor) params.set('doctorName', doctor);

    try {
        const res = await fetch(`/Invoices/PatientCharges?${params}`);
        if (!res.ok) return;
        const data = await res.json();
        const balance = Number(data.balance) || 0;
        balanceDueEl.value = balance.toFixed(2);
        if (balanceStatusEl) balanceStatusEl.value = balance > 0 ? 'Due' : 'Paid';
        updateCashEndingBalance();
    } catch { /* ignore */ }
}

function updateCashEndingBalance() {
    const balanceDueEl = document.querySelector('[name="Input.BalanceDue"]');
    const amountEl = document.querySelector('[name="Input.Amount"]');
    const endingEl = document.querySelector('[name="Input.EndingBalance"]');
    const creditEl = document.querySelector('[name="Input.PatientCredit"]');
    if (!balanceDueEl || !endingEl) return;

    const due = Number(balanceDueEl.value) || 0;
    const amount = Number(amountEl?.value) || 0;
    const applied = Math.min(amount, Math.max(0, due));
    const credit = Math.max(0, amount - applied);
    const remaining = Math.max(0, due - applied);

    endingEl.value = remaining.toFixed(2);
    if (creditEl) creditEl.value = credit.toFixed(2);
}

function bindCashBalanceUpdates() {
    const amountEl = document.querySelector('[name="Input.Amount"]');
    const balanceDueEl = document.querySelector('[name="Input.BalanceDue"]');
    amountEl?.addEventListener('input', updateCashEndingBalance);
    amountEl?.addEventListener('change', updateCashEndingBalance);
    balanceDueEl?.addEventListener('input', updateCashEndingBalance);
    balanceDueEl?.addEventListener('change', updateCashEndingBalance);
    updateCashEndingBalance();
}

async function refreshCashBalanceIfPatientSelected() {
    const patientId = document.querySelector('[name="Input.PatientId"]')?.value?.trim();
    const patientName = document.querySelector('#cashReceiptPatientNameInput, #cashPaymentPatientNameInput')?.value?.trim();
    if (!patientId && !patientName) return;

    let patient = { patientNo: patientId, name: patientName };
    if (patientId && typeof lookupPatientByBarcode === 'function') {
        const found = await lookupPatientByBarcode(patientId);
        if (found) patient = found;
    }

    await loadPatientCashBalance(patient);
}

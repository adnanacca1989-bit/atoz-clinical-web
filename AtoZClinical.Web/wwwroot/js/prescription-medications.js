/** Pharmacy item picker for prescription medication lines (display only — no inventory impact). */
function applyPrescriptionItem(row, itemId) {
    const select = row.querySelector('.prescription-item-select');
    const option = select?.querySelector(`option[value="${itemId}"]`);
    const idInput = row.querySelector('.prescription-pharmacy-id');
    const nameInput = row.querySelector('.prescription-medicine-name');
    const doseInput = row.querySelector('.prescription-dose');
    const unitInput = row.querySelector('.prescription-unit');
    const formInput = row.querySelector('.prescription-med-form');

    if (!itemId || !option) {
        if (idInput) idInput.value = '';
        if (nameInput) nameInput.value = '';
        return;
    }

    if (idInput) idInput.value = itemId;
    if (nameInput) nameInput.value = option.dataset.name || option.textContent.trim();
    if (doseInput && !doseInput.value.trim() && option.dataset.dosage)
        doseInput.value = option.dataset.dosage;
    if (unitInput && !unitInput.value.trim() && option.dataset.baseUom)
        unitInput.value = option.dataset.baseUom;
    if (formInput && !formInput.value.trim() && option.dataset.dosage)
        formInput.placeholder = option.dataset.dosage;
}

function initPrescriptionMedications() {
    const tbody = document.querySelector('.prescription-medication-grid tbody');
    if (!tbody) return;

    tbody.querySelectorAll('tr.prescription-line').forEach(row => {
        const select = row.querySelector('.prescription-item-select');
        const idInput = row.querySelector('.prescription-pharmacy-id');
        if (select && idInput?.value)
            select.value = idInput.value;
        applyPrescriptionItem(row, select?.value || '');
    });

    tbody.addEventListener('change', (e) => {
        const select = e.target.closest('.prescription-item-select');
        if (!select) return;
        const row = select.closest('tr.prescription-line');
        if (row) applyPrescriptionItem(row, select.value);
    });

    document.addEventListener('clinical-line-added', (e) => {
        const row = e.detail?.row;
        if (row?.classList.contains('prescription-line'))
            applyPrescriptionItem(row, '');
    });

    initClinicalLineGrid({
        scrollSelector: '.prescription-medication-scroll',
        rowSelector: 'tr.prescription-line',
        namePrefix: 'Medications'
    });
}

document.addEventListener('DOMContentLoaded', initPrescriptionMedications);

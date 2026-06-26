document.addEventListener('DOMContentLoaded', () => {
    if (!document.querySelector('.invoice-erp-page')) return;

    let invoiceChargesLocked = false;
    window.invoiceChargesLocked = false;

    const lockInvoiceCharges = () => {
        invoiceChargesLocked = true;
        window.invoiceChargesLocked = true;
    };

    initPatientPicker({
        patientNameSelector: '#invoicePatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: patient => {
            invoiceChargesLocked = false;
            window.invoiceChargesLocked = false;
            loadPatientInvoiceCharges(patient);
        }
    });
    initPatientBarcodeScanner({
        barcodeSelector: '#invoicePatientBarcodeInput',
        patientNameSelector: '#invoicePatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: patient => {
            invoiceChargesLocked = false;
            window.invoiceChargesLocked = false;
            loadPatientInvoiceCharges(patient);
        }
    });
    bindInvoiceTotals();
    bindInvoicePrint();
    bindInvoiceServicePickers();

    const tbody = document.querySelector('.invoice-line-grid tbody');
    tbody?.addEventListener('input', e => {
        if (e.target.closest('.invoice-service-name, [name$=".Qty"], [name$=".UnitFee"]'))
            lockInvoiceCharges();
    });
    tbody?.addEventListener('click', e => {
        if (e.target.closest('.delete-line-btn')) lockInvoiceCharges();
    });
    document.addEventListener('clinical-line-removed', lockInvoiceCharges);
});

// Exposed for inline handlers if needed
window.lockInvoiceCharges = () => { window.invoiceChargesLocked = true; };

function bindInvoiceServicePickers() {
    const applyPick = (select) => {
        const row = select.closest('tr');
        if (!row) return;
        const opt = select.selectedOptions[0];
        const nameInput = row.querySelector('.invoice-service-name');
        const feeInput = row.querySelector('[name$=".UnitFee"]');
        if (!opt?.value) return;
        if (nameInput) nameInput.value = opt.value;
        if (feeInput && opt.dataset.fee) feeInput.value = Number(opt.dataset.fee).toFixed(2);
        if (typeof window.recalcInvoiceTotals === 'function') window.recalcInvoiceTotals();
        if (typeof window.lockInvoiceCharges === 'function') window.lockInvoiceCharges();
    };

    document.querySelectorAll('.invoice-service-pick').forEach(select => {
        select.addEventListener('change', () => applyPick(select));
    });

    document.querySelectorAll('.invoice-service-name').forEach(input => {
        input.addEventListener('input', () => {
            const row = input.closest('tr');
            const select = row?.querySelector('.invoice-service-pick');
            if (!select) return;
            const match = [...select.options].find(o => o.value && o.value === input.value.trim());
            select.value = match?.value ?? '';
        });
    });
}

function bindInvoiceTotals() {
    const subTotalEl = document.getElementById('invoiceSubTotal');
    const netEl = document.getElementById('invoiceNetAmount');
    const balanceEl = document.getElementById('invoiceBalance');
    const discountEl = document.getElementById('invoiceDiscount');
    const paidEl = document.getElementById('invoiceAmountPaid');
    const tbody = document.querySelector('.invoice-line-grid tbody');
    if (!subTotalEl || !netEl || !balanceEl || !tbody) return;

    const recalc = () => {
        let sub = 0;
        tbody.querySelectorAll('tr.invoice-line').forEach(row => {
            const qty = Number(row.querySelector('[name$=".Qty"]')?.value) || 0;
            const rate = Number(row.querySelector('[name$=".UnitFee"]')?.value) || 0;
            const total = qty * rate;
            sub += total;
            const totalInput = row.querySelector('.invoice-line-total');
            if (totalInput) totalInput.value = total.toFixed(2);
        });
        const discount = Number(discountEl?.value) || 0;
        const paid = Number(paidEl?.value) || 0;
        const net = sub - discount;
        subTotalEl.value = sub.toFixed(2);
        netEl.value = net.toFixed(2);
        balanceEl.value = (net - paid).toFixed(2);
        syncInvoicePrintDocument();
    };

    tbody.addEventListener('input', recalc);
    discountEl?.addEventListener('input', recalc);
    paidEl?.addEventListener('change', recalc);
    paidEl?.addEventListener('input', recalc);
    document.querySelector('[name="Input.PaymentMethod"]')?.addEventListener('change', syncInvoicePrintDocument);
    document.querySelector('[name="Input.PaymentStatus"]')?.addEventListener('input', syncInvoicePrintDocument);
    document.querySelector('[name="Input.InvoiceDate"]')?.addEventListener('change', syncInvoicePrintDocument);
    document.addEventListener('clinical-line-added', recalc);
    document.addEventListener('clinical-line-removed', recalc);

    recalc();
    window.recalcInvoiceTotals = recalc;
}

function bindInvoicePrint() {
    const printBtn = document.querySelector('.clinical-toolbar button[onclick*="clinicalPrintForm"]');
    if (!printBtn) return;
    printBtn.removeAttribute('onclick');
    printBtn.addEventListener('click', e => {
        e.preventDefault();
        invoicePrintDocument();
    });
}

function syncInvoicePrintDocument() {
    const area = document.getElementById('invoicePrintArea');
    if (!area) return;

    const setText = (id, value) => {
        const el = document.getElementById(id);
        if (el) el.textContent = value ?? '';
    };
    const setVal = (id, value) => {
        const el = document.getElementById(id);
        if (el) el.textContent = value ?? '';
    };

    setText('invPrintNo', document.querySelector('[name="Input.InvoiceNo"]')?.value);
    const dateInput = document.querySelector('[name="Input.InvoiceDate"]');
    if (dateInput?.value) {
        const d = new Date(dateInput.value + 'T12:00:00');
        setText('invPrintDate', isNaN(d.getTime()) ? dateInput.value : d.toLocaleDateString());
    }
    setText('invPrintStatus', document.querySelector('[name="Input.PaymentStatus"]')?.value);
    setText('invPrintPatient', document.querySelector('[name="Input.PatientName"]')?.value);
    setText('invPrintMrn', 'MRN: ' + (document.querySelector('[name="Input.PatientId"]')?.value || '—'));
    const age = document.querySelector('[name="Input.Age"]')?.value || '—';
    const gender = document.querySelector('[name="Input.Gender"]')?.value || '—';
    setText('invPrintDemo', age + ' / ' + gender);
    setText('invPrintPhone', document.querySelector('[name="Input.Phone"]')?.value || '—');
    setText('invPrintCity', document.querySelector('[name="Input.City"]')?.value || '—');
    setText('invPrintDoctor', document.querySelector('[name="Input.DoctorName"]')?.value || '—');
    setText('invPrintSpecialty', document.querySelector('[name="Input.Specialty"]')?.value || '—');
    setText('invPrintMethod', document.querySelector('[name="Input.PaymentMethod"]')?.value);

    setVal('invPrintSubtotal', document.getElementById('invoiceSubTotal')?.value);
    setVal('invPrintDiscount', document.getElementById('invoiceDiscount')?.value);
    setVal('invPrintTotal', document.getElementById('invoiceNetAmount')?.value);
    setVal('invPrintPaid', document.getElementById('invoiceAmountPaid')?.value);
    setVal('invPrintBalance', document.getElementById('invoiceBalance')?.value);

    const tbody = document.getElementById('invPrintLinesBody');
    const srcBody = document.querySelector('.invoice-line-grid tbody');
    if (!tbody || !srcBody) return;

    tbody.innerHTML = '';
    srcBody.querySelectorAll('tr.invoice-line').forEach(row => {
        const name = row.querySelector('.invoice-service-name')?.value?.trim() || '';
        const qty = Number(row.querySelector('[name$=".Qty"]')?.value) || 0;
        const rate = Number(row.querySelector('[name$=".UnitFee"]')?.value) || 0;
        const total = qty * rate;
        if (!name && total <= 0) return;

        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td class="inv-col-no">${row.querySelector('[name$=".LineNo"]')?.value || ''}</td>
            <td class="inv-col-desc">${escapeHtml(name)}</td>
            <td class="inv-col-qty">${qty}</td>
            <td class="inv-col-rate">${rate.toFixed(2)}</td>
            <td class="inv-col-amt">${total.toFixed(2)}</td>`;
        tbody.appendChild(tr);
    });
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function invoicePrintDocument() {
    syncInvoicePrintDocument();
    document.body.classList.add('invoice-printing');
    window.print();
    window.addEventListener('afterprint', () => document.body.classList.remove('invoice-printing'), { once: true });
}

async function loadPatientInvoiceCharges(patient) {
    if (window.invoiceChargesLocked) return;
    const params = new URLSearchParams();
    if (patient.patientNo) params.set('patientBarcode', patient.patientNo);
    if (patient.name) params.set('patientName', patient.name);
    const doctor = patient.doctorName || document.querySelector('[name="Input.DoctorName"]')?.value?.trim();
    if (doctor) params.set('doctorName', doctor);
    try {
        const res = await fetch(`/Invoices/PatientCharges?${params}`);
        if (!res.ok) return;
        const data = await res.json();
        fillInvoiceLines(data.lines || []);
        const paidInput = document.querySelector('[name="Input.AmountPaid"]');
        if (paidInput && data.totalPaid != null) paidInput.value = Number(data.totalPaid).toFixed(2);
        if (typeof window.recalcInvoiceTotals === 'function') window.recalcInvoiceTotals();
    } catch { /* ignore */ }
}

function fillInvoiceLines(lines) {
    const tbody = document.querySelector('.invoice-line-grid tbody');
    if (!tbody) return;

    const ensureRows = (count) => {
        let rows = tbody.querySelectorAll('tr.invoice-line');
        while (rows.length < count) {
            const clone = rows[rows.length - 1].cloneNode(true);
            clone.querySelectorAll('input, select').forEach(el => {
                if (el.readOnly) return;
                else el.value = el.type === 'number' ? (el.name?.includes('.Qty') ? '1' : '0') : '';
            });
            tbody.appendChild(clone);
            rows = tbody.querySelectorAll('tr.invoice-line');
        }
        rows.forEach((row, i) => {
            row.querySelectorAll('[name^="Lines["]').forEach(input => {
                input.name = input.name.replace(/Lines\[\d+\]/, `Lines[${i}]`);
                if (input.id) input.id = input.id.replace(/Lines_\d+__/, `Lines_${i}__`);
            });
            const lineNo = row.querySelector('[name$=".LineNo"]');
            if (lineNo) lineNo.value = String(i + 1);
        });
    };

    ensureRows(Math.max(lines.length, 8));

    const rows = tbody.querySelectorAll('tr.invoice-line');
    rows.forEach((row, i) => {
        const line = lines[i];
        const nameInput = row.querySelector('.invoice-service-name');
        const qtyInput = row.querySelector('[name$=".Qty"]');
        const rateInput = row.querySelector('[name$=".UnitFee"]');

        if (!line) {
            if (nameInput) nameInput.value = '';
            if (qtyInput) qtyInput.value = '1';
            if (rateInput) rateInput.value = '0';
            return;
        }

        if (nameInput) nameInput.value = line.serviceName ?? '';
        if (qtyInput) qtyInput.value = line.qty ?? 1;
        if (rateInput) rateInput.value = line.unitFee ?? 0;
    });

    if (typeof window.recalcInvoiceTotals === 'function') window.recalcInvoiceTotals();
}

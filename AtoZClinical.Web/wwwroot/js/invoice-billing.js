document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#invoicePatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPatientInvoiceCharges
    });
    bindInvoiceTotals();
});
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
    };

    tbody.addEventListener('input', recalc);
    discountEl?.addEventListener('input', recalc);
    paidEl?.addEventListener('change', recalc);
    paidEl?.addEventListener('input', recalc);
    document.addEventListener('clinical-line-added', recalc);
    document.addEventListener('clinical-line-removed', recalc);

    recalc();
    window.recalcInvoiceTotals = recalc;
}

async function loadPatientInvoiceCharges(patient) {
    const params = new URLSearchParams();
    if (patient.patientNo) params.set('patientBarcode', patient.patientNo);
    if (patient.name) params.set('patientName', patient.name);
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

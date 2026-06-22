document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#invoicePatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPatientInvoiceCharges
    });
    initInvoiceLineGrid();
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
    recalc();
    window.recalcInvoiceTotals = recalc;
}

function initInvoiceLineGrid() {
    const tbody = document.querySelector('.invoice-line-grid tbody');
    const addBtn = document.getElementById('addClinicalLinesBtn');
    if (!tbody) return;

    const reindex = () => {
        tbody.querySelectorAll('tr.invoice-line').forEach((row, i) => {
            row.querySelectorAll('[name^="Lines["]').forEach(input => {
                input.name = input.name.replace(/Lines\[\d+\]/, `Lines[${i}]`);
            });
            const lineNo = row.querySelector('[name$=".LineNo"]');
            if (lineNo) lineNo.value = String(i + 1);
        });
    };

    addBtn?.addEventListener('click', () => {
        const rows = tbody.querySelectorAll('tr.invoice-line');
        const last = rows[rows.length - 1];
        if (!last) return;
        const clone = last.cloneNode(true);
        clone.querySelectorAll('input, select').forEach(el => {
            if (el.tagName === 'SELECT') el.selectedIndex = 0;
            else if (el.readOnly) return;
            else el.value = el.type === 'number' ? (el.name?.includes('.Qty') ? '1' : '0') : '';
        });
        tbody.appendChild(clone);
        reindex();
    });

    tbody.addEventListener('click', (e) => {
        const btn = e.target.closest('.delete-line-btn');
        if (!btn) return;
        const row = btn.closest('tr.invoice-line');
        const rows = tbody.querySelectorAll('tr.invoice-line');
        if (rows.length <= 1) {
            row.querySelectorAll('input:not([readonly]), select').forEach(el => {
                if (el.tagName === 'SELECT') el.selectedIndex = 0;
                else el.value = el.type === 'number' ? (el.name?.includes('.Qty') ? '1' : '0') : '';
            });
            return;
        }
        row.remove();
        reindex();
    });
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
                if (el.tagName === 'SELECT') el.selectedIndex = 0;
                else if (!el.readOnly) el.value = el.type === 'number' ? (el.name?.includes('.Qty') ? '1' : '0') : '';
            });
            tbody.appendChild(clone);
            rows = tbody.querySelectorAll('tr.invoice-line');
        }
        rows.forEach((row, i) => {
            row.querySelectorAll('[name^="Lines["]').forEach(input => {
                input.name = input.name.replace(/Lines\[\d+\]/, `Lines[${i}]`);
            });
            const lineNo = row.querySelector('[name$=".LineNo"]');
            if (lineNo) lineNo.value = String(i + 1);
        });
    };

    ensureRows(Math.max(lines.length, 8));

    const rows = tbody.querySelectorAll('tr.invoice-line');
    rows.forEach((row, i) => {
        const line = lines[i];
        const setVal = (suffix, val) => {
            const el = row.querySelector(`[name$="${suffix}"]`);
            if (el) el.value = val ?? '';
        };
        if (!line) {
            setVal('.ServiceName', '');
            setVal('.Qty', '1');
            setVal('.UnitFee', '0');
            return;
        }
        const svcSelect = row.querySelector('[name$=".ServiceName"]');
        if (svcSelect) {
            let found = false;
            for (const opt of svcSelect.options) {
                if (opt.value === line.serviceName || opt.text === line.serviceName) {
                    svcSelect.value = opt.value;
                    found = true;
                    break;
                }
            }
            if (!found) {
                const opt = document.createElement('option');
                opt.value = line.serviceName;
                opt.textContent = line.serviceName;
                opt.selected = true;
                svcSelect.appendChild(opt);
            }
        }
        setVal('.Qty', line.qty ?? 1);
        setVal('.UnitFee', line.unitFee ?? 0);
    });
    if (typeof window.recalcInvoiceTotals === 'function') window.recalcInvoiceTotals();
}

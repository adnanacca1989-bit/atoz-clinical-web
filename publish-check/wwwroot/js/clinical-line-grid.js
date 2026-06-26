/** Add/delete rows on clinical line grids; bind #addClinicalLinesBtn in toolbar. */
function initClinicalLineGrid(options = {}) {
    const scroll = document.querySelector(options.scrollSelector || '.line-grid-scroll');
    const tbody = scroll?.querySelector('tbody') || document.querySelector('.clinical-line-grid tbody, .pharmacy-line-grid tbody, .invoice-line-grid tbody');
    const addBtn = document.getElementById('addClinicalLinesBtn');
    if (!tbody) return;

    const rowSelector = options.rowSelector || 'tr[data-line], tr.pharmacy-line, tr.invoice-line, tr.lab-line, tr.radiology-line, tr.clinical-line';

    const reindexRows = () => {
        tbody.querySelectorAll(rowSelector).forEach((row, i) => {
            row.dataset.line = String(i);
            row.querySelectorAll('[name^="Lines["]').forEach(input => {
                input.name = input.name.replace(/Lines\[\d+\]/, `Lines[${i}]`);
                if (input.id) input.id = input.id.replace(/Lines_\d+__/, `Lines_${i}__`);
            });
            const lineNo = row.querySelector('[name$=".LineNo"]');
            if (lineNo) lineNo.value = String(i + 1);
        });
    };

    const clearRow = (row) => {
        row.querySelectorAll('input, select, textarea').forEach(el => {
            if (el.readOnly && el.name?.endsWith('.LineNo')) return;
            if (el.classList.contains('invoice-service-pick')) { el.selectedIndex = 0; return; }
            if (el.tagName === 'SELECT') el.selectedIndex = 0;
            else if (el.type === 'number') {
                if (el.classList.contains('pharmacy-qty') || el.classList.contains('purchase-qty') || el.name?.includes('.Qty'))
                    el.value = '1';
                else el.value = '0';
            } else if (!el.readOnly) el.value = '';
        });
        row.querySelectorAll('.purchase-line-total, .pharmacy-line-total, .invoice-line-total').forEach(el => { el.value = '0.00'; });
    };

    addBtn?.addEventListener('click', () => {
        const rows = tbody.querySelectorAll(rowSelector);
        const last = rows[rows.length - 1];
        if (!last) return;
        const clone = last.cloneNode(true);
        clearRow(clone);
        tbody.appendChild(clone);
        reindexRows();
        document.dispatchEvent(new CustomEvent('clinical-line-added', { detail: { row: clone } }));
    });

    tbody.addEventListener('click', (e) => {
        const btn = e.target.closest('.delete-line-btn');
        if (!btn) return;
        e.preventDefault();
        const row = btn.closest(rowSelector);
        if (!row) return;
        const rows = tbody.querySelectorAll(rowSelector);
        if (rows.length <= 1) {
            clearRow(row);
            return;
        }
        row.remove();
        reindexRows();
        document.dispatchEvent(new CustomEvent('clinical-line-removed'));
    });
}

document.addEventListener('DOMContentLoaded', () => {
    if (document.querySelector('.line-grid-scroll, .clinical-line-grid, .pharmacy-line-grid, .invoice-line-grid'))
        initClinicalLineGrid();
});

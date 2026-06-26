/** Add/delete rows and widen item column on pharmacy line grids. */
function initPharmacyLineGrid(options = {}) {
    const tbody = document.querySelector(options.tbodySelector || '.pharmacy-line-grid tbody');
    const addBtn = document.getElementById('addPharmacyLinesBtn');
    if (!tbody) return;

    const reindexRows = () => {
        tbody.querySelectorAll('tr.pharmacy-line').forEach((row, i) => {
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
            if (el.tagName === 'SELECT') el.selectedIndex = 0;
            else if (el.type === 'number') {
                if (el.classList.contains('pharmacy-qty') || el.classList.contains('purchase-qty')) el.value = '1';
                else el.value = '0';
            } else if (!el.readOnly) el.value = '';
        });
        const total = row.querySelector('.purchase-line-total, .pharmacy-line-total');
        if (total) total.value = '0.00';
    };

    addBtn?.addEventListener('click', () => {
        const rows = tbody.querySelectorAll('tr.pharmacy-line');
        const last = rows[rows.length - 1];
        if (!last) return;
        const clone = last.cloneNode(true);
        clearRow(clone);
        tbody.appendChild(clone);
        reindexRows();
        document.dispatchEvent(new CustomEvent('pharmacy-line-added', { detail: { row: clone } }));
    });

    tbody.addEventListener('click', (e) => {
        const btn = e.target.closest('.delete-line-btn');
        if (!btn) return;
        e.preventDefault();
        const row = btn.closest('tr.pharmacy-line');
        if (!row) return;
        const rows = tbody.querySelectorAll('tr.pharmacy-line');
        if (rows.length <= 1) {
            clearRow(row);
            return;
        }
        row.remove();
        reindexRows();
        document.dispatchEvent(new CustomEvent('pharmacy-line-removed'));
    });
}

document.addEventListener('DOMContentLoaded', () => {
    if (document.querySelector('.pharmacy-line-grid')) initPharmacyLineGrid();
});

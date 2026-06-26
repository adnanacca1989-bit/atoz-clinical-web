/** Qty × fee line totals for laboratory and radiology request grids. */
function initClinicalRequestLines(config) {
    const rowClass = config.rowClass;
    const qtyClass = config.qtyClass;
    const feeClass = config.feeClass;
    const totalClass = config.totalClass;
    const selectClass = config.selectClass;
    const grandTotalId = config.grandTotalId;
    const fieldMap = config.fieldMap || {};

    const grid = document.querySelector(config.gridSelector || '.clinical-line-grid');
    const tbody = grid?.querySelector('tbody');
    if (!tbody) return;

    const updateRow = (row) => {
        if (!row) return;
        const qty = parseFloat(row.querySelector(`.${qtyClass}`)?.value) || 0;
        const fee = parseFloat(row.querySelector(`.${feeClass}`)?.value) || 0;
        const totalEl = row.querySelector(`.${totalClass}`);
        if (totalEl) totalEl.value = (qty * fee).toFixed(2);
    };

    const updateGrand = () => {
        let sum = 0;
        document.querySelectorAll(`.${rowClass}`).forEach(row => {
            updateRow(row);
            sum += parseFloat(row.querySelector(`.${totalClass}`)?.value) || 0;
        });
        const grand = document.getElementById(grandTotalId);
        if (grand) grand.value = sum.toFixed(2);
    };

    const applySelect = (row, opt) => {
        if (!opt?.dataset?.code) return;
        Object.entries(fieldMap).forEach(([cls, attr]) => {
            const el = row.querySelector(`.${cls}`);
            if (el) el.value = opt.dataset[attr] || '';
        });
        if (opt.dataset.fee) row.querySelector(`.${feeClass}`).value = opt.dataset.fee;
        updateRow(row);
        updateGrand();
    };

    tbody.addEventListener('input', (e) => {
        if (e.target.matches(`.${qtyClass}, .${feeClass}`)) {
            updateRow(e.target.closest(`.${rowClass}`));
            updateGrand();
        }
    });

    tbody.addEventListener('change', (e) => {
        if (e.target.matches(`.${selectClass}`)) {
            applySelect(e.target.closest(`.${rowClass}`), e.target.selectedOptions[0]);
        }
    });

    document.addEventListener('clinical-line-added', updateGrand);
    document.addEventListener('clinical-line-removed', updateGrand);

    document.querySelectorAll(`.${rowClass}`).forEach(row => updateRow(row));
    updateGrand();
}

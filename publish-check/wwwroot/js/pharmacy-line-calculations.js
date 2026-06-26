/** Line totals and summary calculations for pharmacy grids. */
function initPharmacyLineCalculations(options = {}) {
    const grid = document.querySelector(options.gridSelector || '.pharmacy-line-grid');
    if (!grid) return;

    const tbody = grid.querySelector('tbody');
    const rowSelector = options.rowSelector || '.pharmacy-line';
    const qtySel = options.qtySelector || '.pharmacy-qty';
    const rateSel = options.rateSelector || '.pharmacy-price, .pharmacy-cost';
    const totalSel = options.totalSelector || '.pharmacy-line-total, .purchase-line-total';

    const getEl = (id) => (id ? document.getElementById(id) : null);

    const updateRow = (row) => {
        if (!row) return 0;
        const qty = parseFloat(row.querySelector(qtySel)?.value) || 0;
        const rate = parseFloat(row.querySelector(rateSel)?.value) || 0;
        const total = qty * rate;
        const totalEl = row.querySelector(totalSel);
        if (totalEl) totalEl.value = total.toFixed(2);
        return total;
    };

    const sumLines = () => {
        let sum = 0;
        grid.querySelectorAll(rowSelector).forEach(row => { sum += updateRow(row); });
        return sum;
    };

    const updateSummary = () => {
        const sub = sumLines();
        const subEl = getEl(options.subTotalId);
        if (subEl) subEl.value = sub.toFixed(2);

        const discountEl = getEl(options.discountId);
        const paidEl = getEl(options.paidId);
        const netEl = getEl(options.netId);
        const balanceEl = getEl(options.balanceId);
        const grandEl = getEl(options.grandTotalId);

        const discount = parseFloat(discountEl?.value) || 0;
        const paid = parseFloat(paidEl?.value) || 0;
        const net = sub - discount;

        if (netEl) netEl.value = net.toFixed(2);
        if (balanceEl) balanceEl.value = (net - paid).toFixed(2);
        if (grandEl) grandEl.value = sub.toFixed(2);

        if (typeof options.onSubTotal === 'function') options.onSubTotal(sub);
    };

    if (tbody) {
        tbody.addEventListener('input', (e) => {
            if (e.target.matches(`${qtySel}, ${rateSel}`)) updateSummary();
        });
        tbody.addEventListener('change', (e) => {
            if (e.target.matches('.pharmacy-item-select')) {
                setTimeout(updateSummary, 0);
            }
        });
    }

    getEl(options.discountId)?.addEventListener('input', updateSummary);
    getEl(options.paidId)?.addEventListener('input', updateSummary);

    document.addEventListener('clinical-line-added', () => updateSummary());
    document.addEventListener('clinical-line-removed', updateSummary);

    updateSummary();
    window.recalcPharmacyLines = updateSummary;
}

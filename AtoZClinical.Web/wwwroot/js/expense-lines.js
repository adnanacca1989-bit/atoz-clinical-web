function recalcExpenseTotal() {
    let total = 0;
    document.querySelectorAll('.expense-line-grid .expense-amount').forEach(input => {
        total += parseFloat(input.value) || 0;
    });
    const out = document.getElementById('expenseVoucherTotal');
    if (out) out.value = total.toFixed(2);
}

function initExpenseLines() {
    const grid = document.querySelector('.expense-line-grid-scroll');
    if (!grid) return;

    grid.addEventListener('input', (e) => {
        if (e.target.classList.contains('expense-amount'))
            recalcExpenseTotal();
    });

    document.addEventListener('clinical-line-added', recalcExpenseTotal);
    document.addEventListener('clinical-line-removed', recalcExpenseTotal);

    initClinicalLineGrid({
        scrollSelector: '.expense-line-grid-scroll',
        rowSelector: 'tr.expense-line',
        namePrefix: 'Lines'
    });

    recalcExpenseTotal();
}

document.addEventListener('DOMContentLoaded', initExpenseLines);

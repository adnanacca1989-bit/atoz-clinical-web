/** Toggle barcode / code / dosage columns on compact pharmacy grids. */
document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('.pharmacy-toggle-details').forEach(btn => {
        const grid = btn.closest('.clinical-form-grid-area')?.querySelector('.pharmacy-line-grid-compact')
            || document.querySelector('.pharmacy-line-grid-compact');
        if (!grid) return;

        const syncLabel = () => {
            const on = grid.classList.contains('pharmacy-show-details');
            btn.textContent = on ? 'Hide details' : 'Show barcode & details';
            btn.setAttribute('aria-pressed', on ? 'true' : 'false');
        };

        btn.addEventListener('click', () => {
            grid.classList.toggle('pharmacy-show-details');
            syncLabel();
        });
        syncLabel();
    });
});

/** Toggle optional detail columns on compact clinical line grids. */
document.addEventListener('DOMContentLoaded', () => {
    const configs = [
        { btnClass: 'pharmacy-toggle-details', gridClass: 'pharmacy-line-grid-compact', showClass: 'pharmacy-show-details', showLabel: 'Show barcode & details', hideLabel: 'Hide details' },
        { btnClass: 'lab-toggle-details', gridClass: 'lab-line-grid-compact', showClass: 'lab-show-details', showLabel: 'Show test details', hideLabel: 'Hide details' },
        { btnClass: 'radiology-toggle-details', gridClass: 'radiology-line-grid-compact', showClass: 'rad-show-details', showLabel: 'Show study details', hideLabel: 'Hide details' }
    ];

    configs.forEach(cfg => {
        document.querySelectorAll('.' + cfg.btnClass).forEach(btn => {
            const grid = btn.closest('.clinical-form-grid-area')?.querySelector('.' + cfg.gridClass)
                || document.querySelector('.' + cfg.gridClass);
            if (!grid) return;

            const syncLabel = () => {
                const on = grid.classList.contains(cfg.showClass);
                btn.textContent = on ? cfg.hideLabel : cfg.showLabel;
                btn.setAttribute('aria-pressed', on ? 'true' : 'false');
            };

            btn.addEventListener('click', () => {
                grid.classList.toggle(cfg.showClass);
                syncLabel();
            });
            syncLabel();
        });
    });
});

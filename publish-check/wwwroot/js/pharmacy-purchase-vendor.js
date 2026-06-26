document.addEventListener('DOMContentLoaded', () => {
    const supplierSelect = document.getElementById('supplierSelect');
    const phoneInput = document.getElementById('supplierPhoneInput');
    if (!supplierSelect || !phoneInput) return;

    const applySupplier = () => {
        const opt = supplierSelect.options[supplierSelect.selectedIndex];
        phoneInput.value = opt?.dataset?.phone || '';
    };

    supplierSelect.addEventListener('change', applySupplier);
    applySupplier();
});

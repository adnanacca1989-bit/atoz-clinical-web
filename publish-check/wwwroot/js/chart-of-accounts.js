document.addEventListener('DOMContentLoaded', () => {
    const categorySelect = document.getElementById('chartCategoryType');
    const detailSelect = document.getElementById('chartDetailType');
    const map = window.chartAccountDetailTypes || {};

    if (!categorySelect || !detailSelect) return;

    function refreshDetailTypes(preserveSelection) {
        const category = categorySelect.value;
        const types = map[category] || [];
        const current = preserveSelection ? detailSelect.value : '';

        detailSelect.innerHTML = '<option value="">--</option>';
        types.forEach(type => {
            const option = document.createElement('option');
            option.value = type;
            option.textContent = type;
            if (type === current) option.selected = true;
            detailSelect.appendChild(option);
        });

        if (current && !types.includes(current)) {
            const legacy = document.createElement('option');
            legacy.value = current;
            legacy.textContent = current;
            legacy.selected = true;
            detailSelect.appendChild(legacy);
        }
    }

    categorySelect.addEventListener('change', () => refreshDetailTypes(false));
    refreshDetailTypes(true);
});

/** Prevent duplicate form submissions and ensure Add creates a new record. */
(function () {
    function clearRecordIdForNewSave(form) {
        const saveMode = form.querySelector('#saveModeInput');
        if (saveMode?.value === 'New') {
            const recordId = form.querySelector('[name="RecordId"]');
            if (recordId) recordId.value = '';
        }
    }

    function lockFormSubmits(form) {
        if (form.dataset.submitLocked === '1') return false;
        form.dataset.submitLocked = '1';
        form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(btn => {
            btn.disabled = true;
            btn.classList.add('is-submitting');
        });
        return true;
    }

    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('form').forEach(form => {
            form.addEventListener('submit', (e) => {
                clearRecordIdForNewSave(form);
                if (!lockFormSubmits(form)) {
                    e.preventDefault();
                    return false;
                }
            });
        });

        document.querySelectorAll('[onclick*="saveModeInput"][onclick*="New"]').forEach(btn => {
            btn.addEventListener('click', () => {
                const form = btn.closest('form');
                const recordId = form?.querySelector('[name="RecordId"]');
                if (recordId) recordId.value = '';
            });
        });
    });
})();

/** Prevent duplicate form submissions and ensure Add creates a new record. */
(function () {
    function isAddHandler(submitter) {
        if (!submitter) return false;
        const name = submitter.getAttribute('name') || '';
        const value = submitter.getAttribute('value') || '';
        if (name === 'handler' && value === 'Add') return true;
        const formaction = submitter.getAttribute('formaction') || '';
        return /(?:\?|&)handler=Add(?:&|$)/i.test(formaction);
    }

    function clearRecordIdForNewSave(form, submitter) {
        const saveMode = form.querySelector('#saveModeInput');
        const isNew = isAddHandler(submitter) || saveMode?.value === 'New';
        if (isNew) {
            if (saveMode) saveMode.value = 'New';
            const recordId = form.querySelector('[name="RecordId"]');
            if (recordId) recordId.value = '';
        }
    }

    function unlockFormSubmits(form) {
        delete form.dataset.submitLocked;
        form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(btn => {
            btn.disabled = false;
            btn.classList.remove('is-submitting');
        });
    }

    function isFormValid(form, submitter) {
        if (submitter?.hasAttribute('formnovalidate')) {
            return true;
        }

        const $ = window.jQuery;
        if ($) {
            const $form = $(form);
            if ($form.data('validator')) {
                return $form.valid();
            }
        }

        return form.checkValidity();
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
                clearRecordIdForNewSave(form, e.submitter);

                if (!isFormValid(form, e.submitter)) {
                    unlockFormSubmits(form);
                    return;
                }

                if (!lockFormSubmits(form)) {
                    e.preventDefault();
                    return false;
                }
            });

            form.addEventListener('invalid', () => {
                unlockFormSubmits(form);
            }, true);
        });

        document.querySelectorAll('[onclick*="saveModeInput"][onclick*="New"]').forEach(btn => {
            btn.addEventListener('click', () => {
                const form = btn.closest('form');
                const recordId = form?.querySelector('[name="RecordId"]');
                if (recordId) recordId.value = '';
            });
        });
    });

    window.addEventListener('pageshow', (e) => {
        if (e.persisted) {
            document.querySelectorAll('form[data-submit-locked="1"]').forEach(unlockFormSubmits);
        }
    });
})();

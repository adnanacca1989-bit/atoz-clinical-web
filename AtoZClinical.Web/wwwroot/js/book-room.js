document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('bookRoomForm');
    const enterDate = document.getElementById('bookEnterDate');
    const exitDate = document.getElementById('bookExitDate');
    const daysInput = document.getElementById('bookDays');
    const exitDateError = document.getElementById('bookExitDateError');

    const stayDateErrorMessage = 'Exit date cannot be before enter date.';

    const isStayDateRangeInvalid = () => {
        if (!enterDate?.value || !exitDate?.value) return false;
        return exitDate.value < enterDate.value;
    };

    const showStayDateError = (message) => {
        if (exitDateError) {
            exitDateError.textContent = message || '';
        }
        exitDate?.classList.toggle('input-validation-error', !!message);
    };

    const validateStayDates = () => {
        const invalid = isStayDateRangeInvalid();
        showStayDateError(invalid ? stayDateErrorMessage : '');
        return !invalid;
    };

    const recomputeDays = () => {
        if (!enterDate || !exitDate || !daysInput) return;
        const enter = enterDate.value;
        const exit = exitDate.value;
        if (!enter || !exit) {
            daysInput.value = '';
            validateStayDates();
            return;
        }
        if (isStayDateRangeInvalid()) {
            daysInput.value = '';
            validateStayDates();
            return;
        }
        const start = new Date(enter);
        const end = new Date(exit);
        const diff = Math.round((end - start) / (1000 * 60 * 60 * 24));
        daysInput.value = diff >= 0 ? diff + 1 : '';
        showStayDateError('');
    };

    enterDate?.addEventListener('change', recomputeDays);
    exitDate?.addEventListener('change', recomputeDays);
    recomputeDays();

    form?.addEventListener('submit', (e) => {
        if (!validateStayDates()) {
            e.preventDefault();
            e.stopPropagation();
            exitDate?.focus();
        }
    });

    initPatientPicker({
        patientNameSelector: '#bookRoomPatientNameInput',
        onApply: async (patient) => {
            await applySurgeryDetails(patient.id);
        },
        fieldMap: {
            '#bookRoomPatientBarcodeInput': p => p.patientNo || '',
            '[name="Input.Age"]': p => p.age != null ? p.age : '',
            '[name="Input.City"]': p => p.city || '',
            '[name="Input.NationalId"]': p => p.nationalId || '',
            '[name="Input.Phone"]': p => p.phone || '',
            '[name="Input.MotherName"]': p => p.motherName || '',
            '[name="Input.DoctorName"]': p => p.doctorName || '',
            '[name="Input.Specialty"]': p => p.specialty || '',
            '#bookRoomPatientRecordId': p => p.id || ''
        }
    });

    initPatientBarcodeScanner({
        barcodeSelector: '#bookRoomPatientBarcodeInput',
        patientNameSelector: '#bookRoomPatientNameInput',
        onApply: async (patient) => {
            await applySurgeryDetails(patient.id);
        },
        fieldMap: {
            '#bookRoomPatientBarcodeInput': p => p.patientNo || '',
            '[name="Input.Age"]': p => p.age != null ? p.age : '',
            '[name="Input.City"]': p => p.city || '',
            '[name="Input.NationalId"]': p => p.nationalId || '',
            '[name="Input.Phone"]': p => p.phone || '',
            '[name="Input.MotherName"]': p => p.motherName || '',
            '[name="Input.DoctorName"]': p => p.doctorName || '',
            '[name="Input.Specialty"]': p => p.specialty || '',
            '#bookRoomPatientRecordId': p => p.id || ''
        }
    });
});

async function applySurgeryDetails(patientId) {
    if (!patientId) return;
    try {
        const res = await fetch(`/Surgery/Lookup?patientId=${encodeURIComponent(patientId)}`);
        if (!res.ok) return;
        const data = await res.json();
        if (!data.found) return;

        setVal('#bookRoomDoctorSurgeryId', data.surgeryId);
        setVal('[name="Input.TypeOfSurgery"]', data.typeOfSurgery);
        setVal('[name="Input.Classify"]', data.classify);
        setVal('[name="Input.SurgeryName"]', data.surgeryName);
        if (data.doctorName) setVal('[name="Input.DoctorName"]', data.doctorName);
        if (data.specialty) setVal('[name="Input.Specialty"]', data.specialty);
        if (data.motherName) setVal('[name="Input.MotherName"]', data.motherName);
    } catch {
        /* ignore */
    }
}

function setVal(selector, value) {
    const el = document.querySelector(selector);
    if (el) el.value = value ?? '';
}

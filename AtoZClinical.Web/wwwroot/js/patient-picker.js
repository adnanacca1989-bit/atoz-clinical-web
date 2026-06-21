/**
 * Shared patient selection modal. Call initPatientPicker({ ... }) on DOMContentLoaded.
 * @param {object} options
 * @param {string} options.patientNameSelector - CSS selector for patient name input
 * @param {Record<string, (p: object) => string|number|null|undefined>} [options.fieldMap] - selector -> value getter
 * @param {(patient: object) => Promise<void>|void} [options.onApply] - called after fields are filled, before modal closes
 */
function initPatientPicker(options) {
    const modalEl = document.getElementById('patientSelectModal');
    const patientNameInput = document.querySelector(options.patientNameSelector);
    if (!modalEl || !patientNameInput) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    const searchInput = document.getElementById('patientSearchInput');
    const tableBody = document.getElementById('patientSelectTableBody');
    const fieldMap = options.fieldMap || {};

    let patients = [];
    let selectedPatient = null;

    const escapeHtml = (s) => {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    };

    const renderPatients = () => {
        if (!tableBody) return;
        tableBody.innerHTML = '';
        patients.forEach(p => {
            const tr = document.createElement('tr');
            if (selectedPatient?.id === p.id) tr.classList.add('selected');
            tr.innerHTML = `
                <td>${escapeHtml(p.patientNo)}</td>
                <td>${escapeHtml(p.name)}</td>
                <td>${escapeHtml(p.gender || '')}</td>
                <td>${p.age != null ? p.age + ' Years' : ''}</td>
                <td>${escapeHtml(p.phone || '')}</td>
                <td>${escapeHtml(p.city || '')}</td>
                <td>${escapeHtml(p.doctorName || '')}</td>
                <td>${escapeHtml(p.specialty || '')}</td>
                <td>${escapeHtml(p.appointmentDate || '')}</td>
                <td>${escapeHtml(p.appointmentTime || '')}</td>
                <td>${escapeHtml(p.status || '')}</td>`;
            tr.addEventListener('click', () => {
                tableBody.querySelectorAll('tr').forEach(r => r.classList.remove('selected'));
                tr.classList.add('selected');
                selectedPatient = p;
            });
            tr.addEventListener('dblclick', () => { selectedPatient = p; applyPatient(); });
            tableBody.appendChild(tr);
        });
    };

    const loadPatients = async () => {
        const q = searchInput?.value?.trim() || '';
        try {
            const res = await fetch(`/PatientRegistration/Lookup?search=${encodeURIComponent(q)}`);
            if (!res.ok) return;
            patients = await res.json();
            selectedPatient = null;
            renderPatients();
        } catch { /* ignore */ }
    };

    const applyPatient = async () => {
        if (!selectedPatient) return;
        patientNameInput.value = selectedPatient.name || '';
        for (const [selector, getter] of Object.entries(fieldMap)) {
            const el = document.querySelector(selector);
            if (!el) continue;
            const val = getter(selectedPatient);
            el.value = val != null ? val : '';
        }
        if (options.onApply) await options.onApply(selectedPatient);
        modal.hide();
    };

    const openPatientSelect = () => { modal.show(); loadPatients(); };

    patientNameInput.addEventListener('click', openPatientSelect);
    patientNameInput.addEventListener('focus', (e) => {
        e.preventDefault();
        patientNameInput.blur();
        openPatientSelect();
    });
    document.getElementById('openPatientSelectBtn')?.addEventListener('click', openPatientSelect);
    searchInput?.addEventListener('input', () => {
        clearTimeout(searchInput._timer);
        searchInput._timer = setTimeout(loadPatients, 250);
    });
    document.getElementById('patientSelectAddBtn')?.addEventListener('click', applyPatient);
    document.getElementById('patientSelectRefreshBtn')?.addEventListener('click', loadPatients);
    document.getElementById('patientSelectClearBtn')?.addEventListener('click', () => {
        if (searchInput) searchInput.value = '';
        selectedPatient = null;
        loadPatients();
    });
    modalEl.addEventListener('shown.bs.modal', () => searchInput?.focus());
}

/** Standard patient field selectors used across clinical forms. */
const patientFieldMap = {
    barcode: '[name="Input.PatientBarcode"]',
    patientId: '[name="Input.PatientId"]',
    age: '[name="Input.Age"]',
    gender: '[name="Input.Gender"]',
    phone: '[name="Input.Phone"]',
    city: '[name="Input.City"]',
    doctor: '[name="Input.DoctorName"]',
    specialty: '[name="Input.Specialty"]',
    appointmentDate: '[name="Input.AppointmentDate"]',
    appointmentTime: '[name="Input.AppointmentTime"]'
};

function standardPatientFieldMap(usePatientIdForBarcode) {
    const barcodeSelector = usePatientIdForBarcode ? patientFieldMap.patientId : patientFieldMap.barcode;
    return {
        [barcodeSelector]: p => p.patientNo || '',
        [patientFieldMap.age]: p => p.age != null ? p.age : '',
        [patientFieldMap.gender]: p => p.gender || '',
        [patientFieldMap.phone]: p => p.phone || '',
        [patientFieldMap.city]: p => p.city || '',
        [patientFieldMap.doctor]: p => p.doctorName || '',
        [patientFieldMap.specialty]: p => p.specialty || ''
    };
}

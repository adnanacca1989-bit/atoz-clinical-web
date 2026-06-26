/**
 * Shared patient selection modal. Call initPatientPicker({ ... }) on DOMContentLoaded.
 */
function formatDateForInput(value) {
    if (!value) return '';
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return '';
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
}

function formatTimeForInput(value) {
    if (!value) return '';
    const d = new Date(`2000-01-01 ${value}`);
    if (Number.isNaN(d.getTime())) return '';
    return d.toTimeString().slice(0, 5);
}

/**
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
                <td>${escapeHtml(p.motherName || '')}</td>
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
        const fromDate = document.getElementById('patientFilterFromDate')?.value || '';
        const toDate = document.getElementById('patientFilterToDate')?.value || '';
        const status = document.getElementById('patientFilterStatus')?.value || 'All';
        const sortBy = document.getElementById('patientFilterSort')?.value || 'recent';
        const params = new URLSearchParams();
        if (q) params.set('search', q);
        if (fromDate) params.set('fromDate', fromDate);
        if (toDate) params.set('toDate', toDate);
        if (status && status !== 'All') params.set('status', status);
        if (sortBy) params.set('sortBy', sortBy);
        try {
            const res = await fetch(`/PatientRegistration/Lookup?${params}`);
            if (!res.ok) return;
            patients = await res.json();
            selectedPatient = null;
            renderPatients();
            if (window.applyClinicalArabic) window.applyClinicalArabic(modalEl);
        } catch { /* ignore */ }
    };

    const applyPatient = async () => {
        if (!selectedPatient) return;
        patientNameInput.value = selectedPatient.name || '';
        for (const [selector, getter] of Object.entries(fieldMap)) {
            const el = document.querySelector(selector);
            if (!el) continue;
            let val = getter(selectedPatient);
            if (el.type === 'date') val = formatDateForInput(val);
            else if (el.type === 'time') val = formatTimeForInput(val);
            el.value = val != null ? val : '';
        }
        if (options.onApply) await options.onApply(selectedPatient);
        modal.hide();
    };

    const showPatientSelectModal = () => {
        modal.show();
        loadPatients();
    };

    const openPatientSelectFromName = () => {
        if (options.disableNameClick) return;
        showPatientSelectModal();
    };

    if (!options.disableNameClick) {
        patientNameInput.addEventListener('click', openPatientSelectFromName);
        patientNameInput.addEventListener('focus', (e) => {
            e.preventDefault();
            patientNameInput.blur();
            openPatientSelectFromName();
        });
    }
    document.getElementById('openPatientSelectBtn')?.addEventListener('click', showPatientSelectModal);
    searchInput?.addEventListener('input', () => {
        clearTimeout(searchInput._timer);
        searchInput._timer = setTimeout(loadPatients, 250);
    });
    ['patientFilterFromDate', 'patientFilterToDate', 'patientFilterStatus', 'patientFilterSort'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', loadPatients);
    });
    document.getElementById('patientSelectAddBtn')?.addEventListener('click', applyPatient);
    document.getElementById('patientSelectRefreshBtn')?.addEventListener('click', loadPatients);
    document.getElementById('patientSelectClearBtn')?.addEventListener('click', () => {
        if (searchInput) searchInput.value = '';
        const fromEl = document.getElementById('patientFilterFromDate');
        const toEl = document.getElementById('patientFilterToDate');
        const statusEl = document.getElementById('patientFilterStatus');
        const sortEl = document.getElementById('patientFilterSort');
        if (fromEl) fromEl.value = '';
        if (toEl) toEl.value = '';
        if (statusEl) statusEl.value = 'All';
        if (sortEl) sortEl.value = 'recent';
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
        [patientFieldMap.specialty]: p => p.specialty || '',
        [patientFieldMap.appointmentDate]: p => p.appointmentDate || '',
        [patientFieldMap.appointmentTime]: p => p.appointmentTime || ''
    };
}

async function lookupPatientByBarcode(barcode) {
    const term = (barcode || '').trim();
    if (!term) return null;
    try {
        const res = await fetch(`/PatientRegistration/Lookup?search=${encodeURIComponent(term)}`);
        if (!res.ok) return null;
        const patients = await res.json();
        if (!Array.isArray(patients) || patients.length === 0) return null;
        const exact = patients.find(p =>
            (p.patientNo || '').toLowerCase() === term.toLowerCase());
        return exact || patients[0];
    } catch {
        return null;
    }
}

function fillPatientFields(patient, fieldMap, patientNameInput) {
    if (!patient) return;
    if (patientNameInput) patientNameInput.value = patient.name || '';
    for (const [selector, getter] of Object.entries(fieldMap || {})) {
        const el = document.querySelector(selector);
        if (!el) continue;
        let val = getter(patient);
        if (el.type === 'date') val = formatDateForInput(val);
        else if (el.type === 'time') val = formatTimeForInput(val);
        el.value = val != null ? val : '';
    }
}

/**
 * Scan patient barcode (PatientNo) and auto-fill the form.
 * @param {object} options
 * @param {string} options.barcodeSelector
 * @param {string} [options.patientNameSelector]
 * @param {Record<string, function>} [options.fieldMap]
 * @param {(patient: object) => Promise<void>|void} [options.onApply]
 */
function initPatientBarcodeScanner(options) {
    const barcodeInput = document.querySelector(options.barcodeSelector);
    if (!barcodeInput) return;

    const patientNameInput = options.patientNameSelector
        ? document.querySelector(options.patientNameSelector)
        : null;
    const fieldMap = options.fieldMap || {};
    let lastScanned = '';

    const scan = async () => {
        const code = barcodeInput.value?.trim();
        if (!code || code === lastScanned) return;
        const patient = await lookupPatientByBarcode(code);
        if (!patient) return;
        lastScanned = code;
        fillPatientFields(patient, fieldMap, patientNameInput);
        if (options.onApply) await options.onApply(patient);
    };

    barcodeInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            lastScanned = '';
            scan();
        }
    });
    barcodeInput.addEventListener('change', () => {
        lastScanned = '';
        scan();
    });
}

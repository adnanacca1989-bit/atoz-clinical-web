document.addEventListener('DOMContentLoaded', () => {
    initRadiologyRequestPatientPicker();
    initRadiologyTestLines();
    initPatientBarcodeScanner({
        barcodeSelector: '#radiologyRequestPatientBarcodeInput',
        patientNameSelector: '#radiologyRequestPatientNameInput',
        fieldMap: standardPatientFieldMap(false)
    });
});

function initRadiologyRequestPatientPicker() {
    const modalEl = document.getElementById('patientSelectModal');
    const patientNameInput = document.getElementById('radiologyRequestPatientNameInput');
    if (!modalEl || !patientNameInput) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    const searchInput = document.getElementById('patientSearchInput');
    const tableBody = document.getElementById('patientSelectTableBody');
    const barcodeInput = document.querySelector('[name="Input.PatientBarcode"]');
    const ageInput = document.querySelector('[name="Input.Age"]');
    const genderInput = document.querySelector('[name="Input.Gender"]');
    const phoneInput = document.querySelector('[name="Input.Phone"]');
    const cityInput = document.querySelector('[name="Input.City"]');
    const doctorInput = document.querySelector('[name="Input.DoctorName"]');
    const specialtyInput = document.querySelector('[name="Input.Specialty"]');

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

    const applyPatient = () => {
        if (!selectedPatient) return;
        patientNameInput.value = selectedPatient.name || '';
        if (barcodeInput) barcodeInput.value = selectedPatient.patientNo || '';
        if (ageInput) ageInput.value = selectedPatient.age != null ? selectedPatient.age : '';
        if (genderInput) genderInput.value = selectedPatient.gender || '';
        if (phoneInput) phoneInput.value = selectedPatient.phone || '';
        if (cityInput) cityInput.value = selectedPatient.city || '';
        if (doctorInput) doctorInput.value = selectedPatient.doctorName || '';
        if (specialtyInput) specialtyInput.value = selectedPatient.specialty || '';
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

function initRadiologyTestLines() {
    initClinicalRequestLines({
        rowClass: 'radiology-line',
        qtyClass: 'radiology-qty',
        feeClass: 'radiology-fee',
        totalClass: 'radiology-line-total',
        selectClass: 'radiology-test-select',
        grandTotalId: 'radiologyRequestTotalAmount',
        fieldMap: {
            'radiology-test-code': 'code',
            'radiology-test-name': 'name',
            'radiology-test-category': 'category'
        }
    });
}

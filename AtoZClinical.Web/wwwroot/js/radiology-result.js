document.addEventListener('DOMContentLoaded', () => {
    const modalEl = document.getElementById('patientSelectModal');
    const patientNameInput = document.getElementById('radiologyResultPatientNameInput');
    if (!modalEl || !patientNameInput) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    const searchInput = document.getElementById('patientSearchInput');
    const tableBody = document.getElementById('patientSelectTableBody');
    const requestNoInput = document.querySelector('[name="Input.RequestNo"]');
    const resultDateInput = document.querySelector('[name="Input.ResultDate"]');
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

    const setLineField = (index, field, value) => {
        const el = document.querySelector(`[name="Lines[${index}].${field}"]`);
        if (el) el.value = value ?? '';
    };

    const clearResultLines = () => {
        for (let i = 0; i < 20; i++) {
            if (!document.querySelector(`[name="Lines[${i}].TestCode"]`)) break;
            setLineField(i, 'LineNo', i + 1);
            ['TestCode', 'TestName', 'Category', 'Result', 'NormalRange', 'Unit'].forEach(f => setLineField(i, f, ''));
        }
    };

    const fillResultLines = (lines) => {
        clearResultLines();
        lines.forEach((line, i) => {
            setLineField(i, 'LineNo', line.lineNo ?? i + 1);
            setLineField(i, 'TestCode', line.testCode);
            setLineField(i, 'TestName', line.testName);
            setLineField(i, 'Category', line.category);
        });
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

    const loadRadiologyRequest = async (patient) => {
        const params = new URLSearchParams();
        if (patient.name) params.set('patientName', patient.name);
        if (patient.patientNo) params.set('patientBarcode', patient.patientNo);
        try {
            const res = await fetch(`/Radiology/RequestByPatient?${params}`);
            if (!res.ok) return;
            const data = await res.json();
            if (!data) return;
            if (requestNoInput) requestNoInput.value = data.requestNo ?? '';
            if (resultDateInput) resultDateInput.value = new Date().toISOString().slice(0, 10);
            if (doctorInput) doctorInput.value = data.doctorName || patient.doctorName || doctorInput.value || '';
            if (specialtyInput) specialtyInput.value = data.specialty || patient.specialty || specialtyInput.value || '';
            if (data.lines?.length) fillResultLines(data.lines);
        } catch { /* ignore */ }
    };

    const applyPatient = async () => {
        if (!selectedPatient) return;
        patientNameInput.value = selectedPatient.name || '';
        if (barcodeInput) barcodeInput.value = selectedPatient.patientNo || '';
        if (ageInput) ageInput.value = selectedPatient.age != null ? selectedPatient.age : '';
        if (genderInput) genderInput.value = selectedPatient.gender || '';
        if (phoneInput) phoneInput.value = selectedPatient.phone || '';
        if (cityInput) cityInput.value = selectedPatient.city || '';
        if (doctorInput) doctorInput.value = selectedPatient.doctorName || '';
        if (specialtyInput) specialtyInput.value = selectedPatient.specialty || '';
        if (resultDateInput && !resultDateInput.value) {
            resultDateInput.value = new Date().toISOString().slice(0, 10);
        }
        await loadRadiologyRequest(selectedPatient);
        if (doctorInput && !doctorInput.value) doctorInput.value = selectedPatient.doctorName || '';
        if (specialtyInput && !specialtyInput.value) specialtyInput.value = selectedPatient.specialty || '';
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
});

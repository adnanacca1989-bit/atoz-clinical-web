document.addEventListener('DOMContentLoaded', () => {
    const dobInput = document.getElementById('patientDateOfBirth');
    const ageInput = document.getElementById('patientAge');
    const doctorNameInput = document.getElementById('doctorNameInput');
    const specialtySelect = document.getElementById('specialtySelect');
    const nationalIdInput = document.getElementById('nationalIdInput');
    const phoneInput = document.querySelector('[name="Input.Phone"]');
    const visitInput = document.getElementById('visitNumberInput');
    const recordIdInput = document.querySelector('[name="RecordId"]');

    const calcAge = () => {
        if (!dobInput || !ageInput || !dobInput.value) {
            if (ageInput) ageInput.value = '';
            return;
        }
        const dob = new Date(dobInput.value + 'T00:00:00');
        const today = new Date();
        let age = today.getFullYear() - dob.getFullYear();
        const m = today.getMonth() - dob.getMonth();
        if (m < 0 || (m === 0 && today.getDate() < dob.getDate())) age--;
        ageInput.value = age >= 0 ? age : '';
    };

    if (dobInput) {
        dobInput.addEventListener('change', calcAge);
        dobInput.addEventListener('input', calcAge);
        calcAge();
    }

    const updateVisitNumber = async () => {
        if (!visitInput) return;
        const nationalId = nationalIdInput?.value?.trim() || '';
        const phone = phoneInput?.value?.trim() || '';
        if (!nationalId && !phone) return;

        const params = new URLSearchParams();
        if (nationalId) params.set('nationalId', nationalId);
        if (phone) params.set('phone', phone);
        if (recordIdInput?.value) params.set('excludeId', recordIdInput.value);

        try {
            const res = await fetch(`/PatientRegistration/VisitInfo?${params}`);
            if (!res.ok) return;
            const data = await res.json();
            if (data.isReturning) {
                visitInput.value = data.nextVisit;
            }
        } catch { /* ignore */ }
    };

    nationalIdInput?.addEventListener('blur', updateVisitNumber);
    phoneInput?.addEventListener('blur', updateVisitNumber);

    // Doctor select modal
    const modalEl = document.getElementById('doctorSelectModal');
    if (!modalEl || !doctorNameInput) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    const searchInput = document.getElementById('doctorSearchInput');
    const tableBody = document.getElementById('doctorSelectTableBody');
    let doctors = [];
    let selectedDoctor = null;

    const renderDoctors = () => {
        if (!tableBody) return;
        tableBody.innerHTML = '';
        doctors.forEach(d => {
            const tr = document.createElement('tr');
            tr.dataset.doctorId = d.id;
            if (selectedDoctor?.id === d.id) tr.classList.add('selected');
            tr.innerHTML = `
                <td>${d.doctorNo}</td>
                <td>${escapeHtml(d.name)}</td>
                <td>${escapeHtml(d.specialty || '')}</td>
                <td>${escapeHtml(d.phone || '')}</td>
                <td>${escapeHtml(d.email || '')}</td>
                <td>${Number(d.consultationFee || 0).toLocaleString(undefined, { minimumFractionDigits: 2 })}</td>
                <td>${escapeHtml(d.status || '')}</td>`;
            tr.addEventListener('click', () => {
                tableBody.querySelectorAll('tr').forEach(r => r.classList.remove('selected'));
                tr.classList.add('selected');
                selectedDoctor = d;
            });
            tr.addEventListener('dblclick', () => {
                selectedDoctor = d;
                applyDoctor();
            });
            tableBody.appendChild(tr);
        });
    };

    const escapeHtml = (s) => {
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    };

    const loadDoctors = async () => {
        const q = searchInput?.value?.trim() || '';
        try {
            const res = await fetch(`/Doctors/Lookup?search=${encodeURIComponent(q)}`);
            if (!res.ok) return;
            doctors = await res.json();
            selectedDoctor = null;
            renderDoctors();
        } catch { /* ignore */ }
    };

    const applyDoctor = () => {
        if (!selectedDoctor) return;
        doctorNameInput.value = selectedDoctor.name;
        if (specialtySelect && selectedDoctor.specialty) {
            for (const opt of specialtySelect.options) {
                if (opt.value === selectedDoctor.specialty || opt.text === selectedDoctor.specialty) {
                    specialtySelect.value = opt.value;
                    break;
                }
            }
            if (!specialtySelect.value) specialtySelect.value = selectedDoctor.specialty;
        }
        modal.hide();
    };

    doctorNameInput.addEventListener('click', () => {
        modal.show();
        loadDoctors();
    });
    doctorNameInput.addEventListener('focus', (e) => {
        e.preventDefault();
        doctorNameInput.blur();
        modal.show();
        loadDoctors();
    });

    searchInput?.addEventListener('input', () => {
        clearTimeout(searchInput._timer);
        searchInput._timer = setTimeout(loadDoctors, 250);
    });

    document.getElementById('doctorSelectAddBtn')?.addEventListener('click', applyDoctor);
    document.getElementById('doctorSelectRefreshBtn')?.addEventListener('click', loadDoctors);
    document.getElementById('doctorSelectClearBtn')?.addEventListener('click', () => {
        if (searchInput) searchInput.value = '';
        selectedDoctor = null;
        loadDoctors();
    });

    modalEl.addEventListener('shown.bs.modal', () => searchInput?.focus());

    // Patient card modal
    const patientCardBtn = document.getElementById('patientCardBtn');
    const patientCardModalEl = document.getElementById('patientCardModal');
    if (patientCardBtn && patientCardModalEl) {
        const patientCardModal = bootstrap.Modal.getOrCreateInstance(patientCardModalEl);
        const patientNameField = document.querySelector('[name="Input.PatientName"]');
        const patientNoField = document.querySelector('[name="Input.PatientNo"]');

        const fillPatientCard = () => {
            const name = patientNameField?.value?.trim() || '—';
            const doctor = doctorNameInput?.value?.trim() || '—';
            const specialty = specialtySelect?.selectedOptions?.[0]?.text?.trim() || specialtySelect?.value || '—';
            const age = ageInput?.value?.trim() || '0';
            const patientId = patientNoField?.value?.trim() || 'PAT-000001';

            document.getElementById('pcPatientName').textContent = name;
            document.getElementById('pcDoctorName').textContent = doctor;
            document.getElementById('pcSpecialty').textContent = specialty === '-- Select --' ? '—' : specialty;
            document.getElementById('pcAge').textContent = age;
            document.getElementById('pcPatientId').textContent = patientId;

            const barcodeEl = document.getElementById('pcBarcode');
            if (barcodeEl && typeof JsBarcode !== 'undefined') {
                JsBarcode(barcodeEl, patientId, {
                    format: 'CODE128',
                    width: 1.5,
                    height: 48,
                    displayValue: false,
                    margin: 0
                });
            }
        };

        patientCardBtn.addEventListener('click', () => {
            fillPatientCard();
            patientCardModal.show();
        });

        document.getElementById('patientCardPrintBtn')?.addEventListener('click', () => {
            fillPatientCard();
            window.print();
        });
    }
});

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
        const patientName = document.querySelector('[name="Input.PatientName"]')?.value?.trim();
        if (patientName) params.set('patientName', patientName);
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
    document.querySelector('[name="Input.PatientName"]')?.addEventListener('blur', updateVisitNumber);

    const escapeHtml = (s) => {
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    };

    // Doctor select modal
    const modalEl = document.getElementById('doctorSelectModal');
    if (modalEl && doctorNameInput) {
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
    }

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

    // Patient visit history modal
    const historyBtn = document.getElementById('patientHistoryBtn');
    const historyModalEl = document.getElementById('patientHistoryModal');
    if (historyBtn && historyModalEl) {
        const historyModal = bootstrap.Modal.getOrCreateInstance(historyModalEl);
        const loadingEl = document.getElementById('patientHistoryLoading');
        const emptyEl = document.getElementById('patientHistoryEmpty');
        const contentEl = document.getElementById('patientHistoryContent');
        const tableBody = document.getElementById('patientHistoryTableBody');
        const grandRevenueEl = document.getElementById('patientHistoryGrandRevenue');
        const grandReceivedEl = document.getElementById('patientHistoryGrandReceived');

        const formatMoney = (n) => Number(n || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

        historyBtn.addEventListener('click', async () => {
            const patientNo = document.querySelector('[name="Input.PatientNo"]')?.value?.trim() || '';
            const patientName = document.querySelector('[name="Input.PatientName"]')?.value?.trim() || '';
            const nationalId = nationalIdInput?.value?.trim() || '';
            const phone = phoneInput?.value?.trim() || '';

            if (!patientNo && !patientName && !nationalId && !phone) {
                alert('Please select or enter a patient before viewing history.');
                return;
            }

            loadingEl?.classList.remove('d-none');
            emptyEl?.classList.add('d-none');
            contentEl?.classList.add('d-none');
            if (tableBody) tableBody.innerHTML = '';
            historyModal.show();

            const params = new URLSearchParams({ handler: 'History' });
            if (patientNo) params.set('patientNo', patientNo);
            if (patientName) params.set('patientName', patientName);
            if (nationalId) params.set('nationalId', nationalId);
            if (phone) params.set('phone', phone);

            try {
                const res = await fetch(`/PatientRegistration?${params}`);
                if (!res.ok) throw new Error('Failed to load history');
                const data = await res.json();
                loadingEl?.classList.add('d-none');

                if (!data.rows || data.rows.length === 0) {
                    emptyEl?.classList.remove('d-none');
                    return;
                }

                data.rows.forEach(row => {
                    const tr = document.createElement('tr');
                    tr.innerHTML = `
                        <td>${escapeHtml(row.visitDate || '')}</td>
                        <td>${escapeHtml(row.doctorName || '—')}</td>
                        <td class="text-end">${formatMoney(row.totalRevenue)}</td>
                        <td class="text-end">${formatMoney(row.amountReceived)}</td>`;
                    tableBody?.appendChild(tr);
                });

                if (grandRevenueEl) grandRevenueEl.textContent = formatMoney(data.grandTotalRevenue);
                if (grandReceivedEl) grandReceivedEl.textContent = formatMoney(data.grandAmountReceived);
                contentEl?.classList.remove('d-none');
            } catch {
                loadingEl?.classList.add('d-none');
                emptyEl?.classList.remove('d-none');
                if (emptyEl) emptyEl.textContent = 'Could not load patient history. Please try again.';
            }
        });
    }

    // Barcode scan — load existing patient record
    const regBarcodeInput = document.getElementById('patientRegistrationBarcodeInput');
    if (regBarcodeInput) {
        const loadPatient = async () => {
            const patient = await lookupPatientByBarcode(regBarcodeInput.value);
            if (patient?.id) window.location.href = `?RecordId=${patient.id}`;
        };
        regBarcodeInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') { e.preventDefault(); loadPatient(); }
        });
        regBarcodeInput.addEventListener('change', loadPatient);
    }

    initPatientPicker({
        patientNameSelector: '[name="Input.PatientName"]',
        disableNameClick: true,
        fieldMap: {},
        onApply: async (patient) => {
            if (!patient?.id) return;
            try {
                const res = await fetch(`/PatientRegistration/CloneInfo?id=${patient.id}`);
                if (!res.ok) return;
                const data = await res.json();
                applyPatientCloneTemplate(data);
            } catch { /* ignore */ }
        }
    });

    function setFieldValue(selector, value) {
        const el = document.querySelector(selector);
        if (!el) return;
        el.value = value ?? '';
    }

    function applyPatientCloneTemplate(data) {
        if (!data) return;

        if (recordIdInput) recordIdInput.value = '';
        const saveModeInput = document.getElementById('saveModeInput');
        if (saveModeInput) saveModeInput.value = 'New';

        setFieldValue('[name="Input.PatientNo"]', data.patientNo);
        setFieldValue('[name="Input.PatientName"]', data.patientName);
        setFieldValue('[name="Input.Gender"]', data.gender);
        setFieldValue('[name="Input.DateOfBirth"]', data.dateOfBirth);
        setFieldValue('[name="Input.Phone"]', data.phone);
        setFieldValue('[name="Input.City"]', data.city);
        setFieldValue('[name="Input.BloodGroup"]', data.bloodGroup);
        setFieldValue('[name="Input.MarriedStatus"]', data.marriedStatus);
        setFieldValue('[name="Input.MotherName"]', data.motherName);
        setFieldValue('[name="Input.DoctorName"]', data.doctorName);
        setFieldValue('[name="Input.Specialty"]', data.specialty);
        setFieldValue('[name="Input.NationalId"]', data.nationalId);
        setFieldValue('[name="Input.Address"]', data.address);
        setFieldValue('[name="Input.EmergencyContact"]', data.emergencyContact);
        setFieldValue('[name="Input.HealthInsuranceName"]', data.healthInsuranceName);
        setFieldValue('[name="Input.HealthInsuranceNumber"]', data.healthInsuranceNumber);
        setFieldValue('[name="Input.AppointmentId"]', data.appointmentId);
        setFieldValue('[name="Input.VisitNumber"]', data.visitNumber);
        setFieldValue('[name="Input.AppointmentDate"]', data.appointmentDate);
        setFieldValue('[name="Input.AppointmentTime"]', data.appointmentTime);
        setFieldValue('[name="Input.Status"]', data.status || 'Pending');

        calcAge();

        document.querySelectorAll('.clinical-record-grid tbody tr').forEach(r => r.classList.remove('selected'));
    }
});

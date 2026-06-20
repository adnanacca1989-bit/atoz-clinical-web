document.addEventListener('DOMContentLoaded', () => {

    initLabRequestPatientPicker();

    initLabTestLines();

});



function initLabRequestPatientPicker() {

    const modalEl = document.getElementById('patientSelectModal');

    const patientNameInput = document.getElementById('labRequestPatientNameInput');

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



function initLabTestLines() {

    const updateLineTotal = (row) => {

        const qty = parseFloat(row.querySelector('.lab-qty')?.value) || 0;

        const fee = parseFloat(row.querySelector('.lab-fee')?.value) || 0;

        const totalCell = row.querySelector('.lab-line-total');

        if (totalCell) totalCell.value = (qty * fee).toFixed(2);

        updateGrandTotal();

    };



    const updateGrandTotal = () => {

        let sum = 0;

        document.querySelectorAll('.lab-line').forEach(row => {

            sum += parseFloat(row.querySelector('.lab-line-total')?.value) || 0;

        });

        const grand = document.getElementById('labRequestTotalAmount');

        if (grand) grand.value = sum.toFixed(2);

    };



    const applyTest = (row, opt) => {

        if (!opt || !opt.dataset.code) return;

        row.querySelector('.lab-test-code').value = opt.dataset.code || '';

        row.querySelector('.lab-test-name').value = opt.dataset.name || '';

        row.querySelector('.lab-test-category').value = opt.dataset.category || '';

        row.querySelector('.lab-fee').value = opt.dataset.fee || '0';

        updateLineTotal(row);

    };



    document.querySelectorAll('.lab-line').forEach(row => {

        const select = row.querySelector('.lab-test-select');

        if (select) {

            select.addEventListener('change', () => {

                const opt = select.options[select.selectedIndex];

                applyTest(row, opt);

            });

        }

        row.querySelector('.lab-qty')?.addEventListener('input', () => updateLineTotal(row));

        row.querySelector('.lab-fee')?.addEventListener('input', () => updateLineTotal(row));

        updateLineTotal(row);

    });

}



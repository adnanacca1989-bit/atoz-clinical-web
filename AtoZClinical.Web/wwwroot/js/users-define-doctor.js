/**
 * Define User — doctor role picker (single init, no duplicate bindings).
 */
function initDefineUserDoctorPicker(options) {
    if (window.__defineUserDoctorPickerInit) return;
    window.__defineUserDoctorPickerInit = true;

    const roleSelect = document.getElementById('userRoleSelect');
    const displayInput = document.getElementById('userDoctorDisplayInput');
    const fullNameInput = document.getElementById('userFullNameInput');
    const doctorRecordIdInput = document.getElementById('userDoctorRecordId');
    const openDoctorBtn = document.getElementById('openDoctorSelectBtn');
    const viewDoctorBtn = document.getElementById('viewDoctorProfileBtn');
    const fullNameLabel = document.getElementById('fullNameLabel');
    const doctorFieldWrap = document.getElementById('doctorFieldWrap');
    const plainFieldWrap = document.getElementById('plainFullNameWrap');
    const form = document.getElementById('defineUserForm');

    if (!roleSelect || !displayInput || !fullNameInput || !doctorRecordIdInput || !form) return;

    const doctorRole = String(options.doctorRoleValue ?? '1');
    let pickerReady = false;

    const isDoctorRole = () => roleSelect.value === doctorRole;
    const hasDoctorLinked = () => Boolean(doctorRecordIdInput.value?.trim());

    const formatDoctorLabel = (d) => {
        if (!d) return '';
        return d.specialty ? `${d.name} — ${d.specialty}` : (d.name || '');
    };

    const updateViewDoctorLink = () => {
        if (!viewDoctorBtn) return;
        const id = doctorRecordIdInput.value?.trim();
        if (isDoctorRole() && id) {
            viewDoctorBtn.href = `/Doctors?RecordId=${encodeURIComponent(id)}`;
            viewDoctorBtn.classList.remove('d-none');
        } else {
            viewDoctorBtn.classList.add('d-none');
            viewDoctorBtn.removeAttribute('href');
        }
    };

    const openDoctorModal = () => {
        ensurePicker();
        openDoctorBtn?.click();
    };

    const ensurePicker = () => {
        if (pickerReady) return;
        initDoctorPicker({
            doctorNameSelector: '#userDoctorDisplayInput',
            openButtonId: 'openDoctorSelectBtn',
            fieldMap: { '#userDoctorRecordId': d => d.id },
            onApply: (d) => {
                displayInput.value = formatDoctorLabel(d);
                updateViewDoctorLink();
            }
        });
        pickerReady = true;
    };

    const setDoctorMode = (autoOpen) => {
        const doctor = isDoctorRole();
        fullNameLabel.textContent = doctor ? 'Doctor (name & specialty) *' : 'Full Name *';
        doctorFieldWrap?.classList.toggle('d-none', !doctor);
        plainFieldWrap?.classList.toggle('d-none', doctor);
        openDoctorBtn?.classList.toggle('d-none', !doctor);

        if (doctor) {
            fullNameInput.removeAttribute('required');
            displayInput.setAttribute('required', 'required');
            ensurePicker();
            updateViewDoctorLink();
            if (autoOpen && !hasDoctorLinked()) {
                setTimeout(openDoctorModal, 200);
            }
        } else {
            displayInput.removeAttribute('required');
            fullNameInput.setAttribute('required', 'required');
            doctorRecordIdInput.value = '';
            displayInput.value = '';
            updateViewDoctorLink();
        }
    };

    roleSelect.addEventListener('change', () => {
        if (!isDoctorRole()) {
            fullNameInput.value = '';
            doctorRecordIdInput.value = '';
            displayInput.value = '';
            setDoctorMode(false);
        } else {
            doctorRecordIdInput.value = '';
            displayInput.value = '';
            setDoctorMode(true);
        }
    });

    displayInput.addEventListener('keydown', (e) => e.preventDefault());
    displayInput.addEventListener('paste', (e) => e.preventDefault());

    openDoctorBtn?.addEventListener('click', () => ensurePicker());

    if (isDoctorRole()) ensurePicker();
    setDoctorMode(true);
}

/**
 * Shared doctor selection modal. Call initDoctorPicker({ ... }) on DOMContentLoaded.
 */
function initDoctorPicker(options) {
    const modalEl = document.getElementById('doctorSelectModal');
    const doctorInput = document.querySelector(options.doctorNameSelector);
    if (!modalEl || !doctorInput) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    const searchInput = document.getElementById('doctorSearchInput');
    const tableBody = document.getElementById('doctorSelectTableBody');
    const specialtySelector = options.specialtySelector ? document.querySelector(options.specialtySelector) : null;
    const fieldMap = options.fieldMap || {};

    let doctors = [];
    let selectedDoctor = null;

    const escapeHtml = (s) => {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    };

    const renderDoctors = () => {
        if (!tableBody) return;
        tableBody.innerHTML = '';
        doctors.forEach(d => {
            const tr = document.createElement('tr');
            if (selectedDoctor?.id === d.id) tr.classList.add('selected');
            tr.innerHTML = `
                <td>${escapeHtml(d.doctorNo)}</td>
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
            tr.addEventListener('dblclick', () => { selectedDoctor = d; applyDoctor(); });
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
        doctorInput.value = selectedDoctor.name || '';
        if (specialtySelector && selectedDoctor.specialty) {
            if (specialtySelector.tagName === 'SELECT') {
                for (const opt of specialtySelector.options) {
                    if (opt.value === selectedDoctor.specialty || opt.text === selectedDoctor.specialty) {
                        specialtySelector.value = opt.value;
                        break;
                    }
                }
                if (!specialtySelector.value) specialtySelector.value = selectedDoctor.specialty;
            } else {
                specialtySelector.value = selectedDoctor.specialty;
            }
        }
        for (const [selector, getter] of Object.entries(fieldMap)) {
            const el = document.querySelector(selector);
            if (!el) continue;
            const val = getter(selectedDoctor);
            el.value = val != null ? val : '';
        }
        if (options.onApply) options.onApply(selectedDoctor);
        modal.hide();
    };

    const openDoctorSelect = () => { modal.show(); loadDoctors(); };

    doctorInput.addEventListener('click', openDoctorSelect);
    doctorInput.addEventListener('focus', (e) => {
        e.preventDefault();
        doctorInput.blur();
        openDoctorSelect();
    });
    document.getElementById(options.openButtonId || 'openDoctorSelectBtn')?.addEventListener('click', openDoctorSelect);
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

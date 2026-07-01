(function () {
    const modalEl = document.getElementById('globalSearchModal');
    const navInput = document.getElementById('globalNavSearchInput');
    const tableBody = document.getElementById('globalSearchTableBody');
    const statusEl = document.getElementById('globalSearchStatus');
    const fromDateInput = document.getElementById('globalSearchFromDate');
    const toDateInput = document.getElementById('globalSearchToDate');
    const transactionTypeInput = document.getElementById('globalSearchTransactionType');
    const patientNameInput = document.getElementById('globalSearchPatientName');
    const doctorNameInput = document.getElementById('globalSearchDoctorName');
    const amountInput = document.getElementById('globalSearchAmount');
    const useDobInput = document.getElementById('globalSearchUseDob');
    const dobInput = document.getElementById('globalSearchDateOfBirth');
    const applyBtn = document.getElementById('globalSearchApplyBtn');
    const clearBtn = document.getElementById('globalSearchClearBtn');

    if (!modalEl || !tableBody || !fromDateInput || !toDateInput) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);

    const escapeHtml = (s) => {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    };

    const formatAmount = (n) => {
        const val = Number(n || 0);
        return val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    };

    const monthStartIso = () => {
        const now = new Date();
        return new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
    };

    const todayIso = () => new Date().toISOString().slice(0, 10);

    const setDefaultFilters = () => {
        fromDateInput.value = monthStartIso();
        toDateInput.value = todayIso();
        transactionTypeInput.value = 'All';
        patientNameInput.value = '';
        doctorNameInput.value = '';
        amountInput.value = '0.00';
        useDobInput.checked = false;
        dobInput.value = todayIso();
        dobInput.disabled = true;
    };

    const buildQuery = () => {
        const params = new URLSearchParams();
        if (fromDateInput.value) params.set('fromDate', fromDateInput.value);
        if (toDateInput.value) params.set('toDate', toDateInput.value);
        if (transactionTypeInput.value) params.set('transactionType', transactionTypeInput.value);
        if (patientNameInput.value.trim()) params.set('patientName', patientNameInput.value.trim());
        if (doctorNameInput.value.trim()) params.set('doctorName', doctorNameInput.value.trim());
        const amount = Number(amountInput.value || 0);
        if (amount > 0) params.set('amount', amount.toString());
        if (useDobInput.checked && dobInput.value) {
            params.set('useDateOfBirth', 'true');
            params.set('dateOfBirth', dobInput.value);
        }
        return params.toString();
    };

    const renderResults = (items) => {
        tableBody.innerHTML = '';
        if (!items || items.length === 0) {
            statusEl.textContent = 'No matching transactions found.';
            return;
        }
        statusEl.textContent = `Showing ${items.length} result${items.length === 1 ? '' : 's'}`;
        items.forEach(item => {
            const tr = document.createElement('tr');
            tr.style.cursor = 'pointer';
            tr.innerHTML = `
                <td>${escapeHtml(String(item.no ?? ''))}</td>
                <td>${escapeHtml(item.date)}</td>
                <td>${escapeHtml(item.type)}</td>
                <td>${escapeHtml(item.patient)}</td>
                <td>${escapeHtml(item.doctor)}</td>
                <td class="text-end">${formatAmount(item.amount)}</td>
                <td>${escapeHtml(item.reference)}</td>
                <td>${escapeHtml(item.details)}</td>`;
            tr.addEventListener('click', () => {
                if (item.link) window.location.href = item.link;
            });
            tableBody.appendChild(tr);
        });
    };

    const runSearch = async () => {
        statusEl.textContent = 'Searching...';
        tableBody.innerHTML = '';
        try {
            const query = buildQuery();
            const res = await fetch(`/Search/Query?${query}`, {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            });
            if (!res.ok) {
                statusEl.textContent = 'Search failed. Please try again.';
                return;
            }
            const data = await res.json();
            renderResults(data.items || []);
        } catch {
            statusEl.textContent = 'Search failed. Please try again.';
        }
    };

    const openSearch = (quickTerm) => {
        if (!fromDateInput.value) setDefaultFilters();
        if (quickTerm) patientNameInput.value = quickTerm;
        modal.show();
        runSearch();
    };

    const clearFilters = () => {
        setDefaultFilters();
        tableBody.innerHTML = '';
        statusEl.textContent = '';
    };

    const initPickers = () => {
        if (typeof initPatientPicker === 'function' && patientNameInput) {
            initPatientPicker({
                patientNameSelector: '#globalSearchPatientName',
                onApply: () => runSearch()
            });
        }
        if (typeof initDoctorPicker === 'function' && doctorNameInput) {
            initDoctorPicker({
                doctorNameSelector: '#globalSearchDoctorName',
                onApply: () => runSearch()
            });
        }
    };

    useDobInput.addEventListener('change', () => {
        dobInput.disabled = !useDobInput.checked;
        if (useDobInput.checked && !dobInput.value) dobInput.value = todayIso();
    });

    applyBtn?.addEventListener('click', runSearch);
    clearBtn?.addEventListener('click', clearFilters);

    modalEl.addEventListener('shown.bs.modal', () => {
        if (!fromDateInput.value) setDefaultFilters();
    });

    if (navInput) {
        navInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                openSearch(navInput.value);
            }
        });
    }

    document.getElementById('globalNavSearchBtn')?.addEventListener('click', () => {
        openSearch(navInput?.value || '');
    });

    initPickers();
})();

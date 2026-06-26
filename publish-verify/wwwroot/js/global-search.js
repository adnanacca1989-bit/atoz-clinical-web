(function () {
    const modalEl = document.getElementById('globalSearchModal');
    const navInput = document.getElementById('globalNavSearchInput');
    const modalInput = document.getElementById('globalSearchInput');
    const tableBody = document.getElementById('globalSearchTableBody');
    const statusEl = document.getElementById('globalSearchStatus');
    if (!modalEl || !navInput || !modalInput || !tableBody) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    let timer = null;

    const escapeHtml = (s) => {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    };

    const formatAmount = (n) => {
        const val = Number(n || 0);
        return val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    };

    const renderResults = (items) => {
        tableBody.innerHTML = '';
        if (!items || items.length === 0) {
            statusEl.textContent = 'No matching transactions found.';
            return;
        }
        statusEl.textContent = `${items.length} result(s)`;
        items.forEach(item => {
            const tr = document.createElement('tr');
            tr.style.cursor = 'pointer';
            tr.innerHTML = `
                <td>${escapeHtml(item.type)}</td>
                <td>${escapeHtml(item.reference)}</td>
                <td>${escapeHtml(item.date)}</td>
                <td>${escapeHtml(item.patient)}</td>
                <td>${escapeHtml(item.doctor)}</td>
                <td class="text-end">${formatAmount(item.amount)}</td>
                <td><a href="${escapeHtml(item.link)}" class="btn btn-sm btn-outline-primary">Open</a></td>`;
            tr.addEventListener('click', (e) => {
                if (e.target.closest('a')) return;
                window.location.href = item.link;
            });
            tableBody.appendChild(tr);
        });
    };

    const runSearch = async (term) => {
        const q = (term || '').trim();
        if (q.length < 2) {
            statusEl.textContent = 'Type at least 2 characters to search.';
            tableBody.innerHTML = '';
            return;
        }
        statusEl.textContent = 'Searching...';
        try {
            const res = await fetch(`/Search/Query?q=${encodeURIComponent(q)}`, {
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

    const openSearch = (term) => {
        modalInput.value = term || navInput.value || '';
        modal.show();
        runSearch(modalInput.value);
        setTimeout(() => modalInput.focus(), 200);
    };

    navInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            openSearch(navInput.value);
        }
    });
    document.getElementById('globalNavSearchBtn')?.addEventListener('click', () => openSearch(navInput.value));

    modalInput.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => runSearch(modalInput.value), 300);
    });
    modalInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            runSearch(modalInput.value);
        }
    });
})();

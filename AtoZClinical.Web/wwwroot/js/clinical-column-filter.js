(function () {
    const modalEl = () => document.getElementById('columnFilterModal');
    const listEl = () => document.getElementById('columnFilterList');
    const titleEl = () => document.getElementById('columnFilterTitle');
    let activeReportKey = null;
    let activeTable = null;
    let activeColumns = [];

    function storageKey(reportKey) {
        return `atzReportCols:${reportKey}`;
    }

    function readVisibility(reportKey, columns) {
        try {
            const raw = localStorage.getItem(storageKey(reportKey));
            if (!raw) return null;
            const parsed = JSON.parse(raw);
            if (!parsed || typeof parsed !== 'object') return null;
            return parsed;
        } catch {
            return null;
        }
    }

    function saveVisibility(reportKey, visibility) {
        localStorage.setItem(storageKey(reportKey), JSON.stringify(visibility));
    }

    function buildColumns(table) {
        const headers = [...table.querySelectorAll('thead th')];
        return headers.map((th, index) => ({
            index,
            key: th.dataset.col || `col${index}`,
            label: th.textContent.trim(),
            defaultVisible: th.dataset.colDefault !== 'false'
        }));
    }

    function applyVisibility(table, columns, visibility) {
        columns.forEach(col => {
            const show = visibility[col.key] !== false;
            const th = table.querySelector(`thead th[data-col="${col.key}"]`) ||
                table.querySelectorAll('thead th')[col.index];
            if (th) th.style.display = show ? '' : 'none';
            table.querySelectorAll('tbody tr').forEach(row => {
                const cell = row.querySelector(`[data-col="${col.key}"]`) || row.children[col.index];
                if (cell) cell.style.display = show ? '' : 'none';
            });
        });
    }

    function currentVisibility(columns) {
        const vis = {};
        columns.forEach(c => { vis[c.key] = c.defaultVisible; });
        return vis;
    }

    function renderModal(columns, visibility) {
        const list = listEl();
        if (!list) return;
        list.innerHTML = '';
        columns.forEach(col => {
            const id = `colFilter_${col.key}`;
            const wrap = document.createElement('div');
            wrap.className = 'form-check mb-1';
            wrap.innerHTML = `
                <input class="form-check-input" type="checkbox" id="${id}" data-col-key="${col.key}" ${visibility[col.key] !== false ? 'checked' : ''}>
                <label class="form-check-label" for="${id}">${col.label}</label>`;
            list.appendChild(wrap);
        });
    }

    window.initReportColumnFilter = function (reportKey, tableSelector) {
        const table = document.querySelector(tableSelector || '.report-grid');
        if (!table || !reportKey) return;

        const columns = buildColumns(table);
        columns.forEach((col, index) => {
            const th = table.querySelectorAll('thead th')[index];
            if (th && !th.dataset.col) th.dataset.col = col.key;
            table.querySelectorAll('tbody tr').forEach(row => {
                const cell = row.children[index];
                if (cell && !cell.dataset.col) cell.dataset.col = col.key;
            });
        });

        let visibility = readVisibility(reportKey, columns) || currentVisibility(columns);
        applyVisibility(table, columns, visibility);

        const btn = document.getElementById('reportColumnFilterBtn');
        if (!btn || btn.dataset.bound === reportKey) return;
        btn.dataset.bound = reportKey;

        btn.addEventListener('click', () => {
            activeReportKey = reportKey;
            activeTable = table;
            activeColumns = buildColumns(table);
            visibility = readVisibility(reportKey, activeColumns) || currentVisibility(activeColumns);
            if (titleEl()) titleEl().textContent = `${reportKey} - Column Filter`;
            renderModal(activeColumns, visibility);
            bootstrap.Modal.getOrCreateInstance(modalEl()).show();
        });
    };

    document.addEventListener('DOMContentLoaded', () => {
        document.getElementById('columnFilterSelectAllBtn')?.addEventListener('click', () => {
            listEl()?.querySelectorAll('input[type=checkbox]').forEach(cb => { cb.checked = true; });
        });
        document.getElementById('columnFilterClearAllBtn')?.addEventListener('click', () => {
            listEl()?.querySelectorAll('input[type=checkbox]').forEach(cb => { cb.checked = false; });
        });
        document.getElementById('columnFilterCancelBtn')?.addEventListener('click', () => {
            bootstrap.Modal.getInstance(modalEl())?.hide();
        });
        document.getElementById('columnFilterApplyBtn')?.addEventListener('click', () => {
            if (!activeReportKey || !activeTable) return;
            const visibility = {};
            listEl()?.querySelectorAll('input[type=checkbox]').forEach(cb => {
                visibility[cb.dataset.colKey] = cb.checked;
            });
            saveVisibility(activeReportKey, visibility);
            applyVisibility(activeTable, activeColumns, visibility);
            bootstrap.Modal.getInstance(modalEl())?.hide();
        });
    });
})();

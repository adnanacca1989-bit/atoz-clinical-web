(function () {
    const grid = document.getElementById('wardRoomGrid');
    const searchInput = document.getElementById('wardSearchInput');
    const table = document.getElementById('wardPatientTable');
    const bookBtn = document.getElementById('wardBookRoomBtn');
    if (!grid || !table) return;

    const rows = Array.from(table.querySelectorAll('tbody tr'));
    const cells = Array.from(grid.querySelectorAll('.ward-room-cell'));
    let selectedRoom = null;

    const normalize = (value) => (value ?? '').toString().trim().toLowerCase();

    const clearSelectedRooms = () => {
        cells.forEach(c => c.classList.remove('ward-room-selected'));
        selectedRoom = null;
    };

    const applyFilters = () => {
        const term = normalize(searchInput?.value);
        rows.forEach(row => {
            const room = normalize(row.dataset.room);
            const patient = normalize(row.dataset.patient);
            const patientId = normalize(row.dataset.patientId);
            const roomSelected = selectedRoom == null || row.dataset.room === String(selectedRoom);
            const termMatch = !term ||
                room.includes(term) ||
                patient.includes(term) ||
                patientId.includes(term);
            row.hidden = !(roomSelected && termMatch);
        });

        const emptyEl = document.getElementById('wardPatientEmpty');
        const visibleCount = rows.filter(r => !r.hidden).length;
        if (emptyEl) {
            emptyEl.hidden = visibleCount > 0;
        } else if (visibleCount === 0 && rows.length > 0) {
            const msg = document.createElement('p');
            msg.id = 'wardPatientEmpty';
            msg.className = 'ward-patient-empty';
            msg.textContent = 'No patients match your search.';
            table.parentElement?.appendChild(msg);
        } else {
            const dynamicEmpty = document.getElementById('wardPatientEmpty');
            if (dynamicEmpty && rows.length > 0) dynamicEmpty.hidden = true;
        }
    };

    const selectRoom = (roomNo) => {
        clearSelectedRooms();
        selectedRoom = roomNo;
        const cell = cells.find(c => c.dataset.room === String(roomNo));
        if (cell) cell.classList.add('ward-room-selected');
        if (bookBtn) bookBtn.href = `/Rooms/Book?RoomNumber=${roomNo}`;
        applyFilters();
    };

    cells.forEach(cell => {
        cell.addEventListener('click', () => {
            const roomNo = Number(cell.dataset.room);
            if (selectedRoom === roomNo) {
                clearSelectedRooms();
                if (bookBtn) bookBtn.href = '/Rooms/Book';
            } else {
                selectRoom(roomNo);
            }
            applyFilters();
        });

        cell.addEventListener('dblclick', () => {
            const roomNo = cell.dataset.room;
            window.location.href = `/Rooms/Book?RoomNumber=${roomNo}`;
        });
    });

    searchInput?.addEventListener('input', applyFilters);

    applyFilters();
})();

(function () {
    const bell = document.getElementById('clinicalNotifyBell');
    const badge = document.getElementById('clinicalNotifyBadge');
    const menu = document.getElementById('clinicalNotifyMenu');
    const list = document.getElementById('clinicalNotifyList');
    if (!bell || !menu || !list) return;

    function render(items, apptCount) {
        list.innerHTML = '';
        if (!items || items.length === 0) {
            list.innerHTML = '<li class="dropdown-item text-muted small">No recent notifications.</li>';
        } else {
            items.forEach(item => {
                const li = document.createElement('li');
                li.className = 'dropdown-item-text clinical-notify-item';
                const link = item.kind === 'appointment'
                    ? '<a href="/Reports/AppointmentReminders" class="stretched-link"></a>'
                    : '';
                li.innerHTML = `<div class="fw-semibold">${item.title || 'Notification'}</div>
                    <div class="small text-muted">${item.detail || ''}</div>
                    <div class="small text-muted">${item.at || ''}</div>${link}`;
                if (item.kind === 'appointment') {
                    li.style.cursor = 'pointer';
                    li.addEventListener('click', () => { window.location.href = '/Reports/AppointmentReminders'; });
                }
                list.appendChild(li);
            });
        }

        const total = (apptCount || 0) + (items ? items.filter(i => i.kind !== 'appointment').length : 0);
        if (badge) {
            if (total > 0) {
                badge.textContent = total > 99 ? '99+' : String(total);
                badge.classList.remove('d-none');
            } else {
                badge.classList.add('d-none');
            }
        }
    }

    async function poll() {
        try {
            const res = await fetch('/Notifications/Feed', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (!res.ok) return;
            const data = await res.json();
            render(data.items || [], data.appointmentCount || 0);
        } catch { /* ignore */ }
    }

    poll();
    setInterval(poll, 60000);
})();

(function () {
    const badge = document.getElementById('apptReminderBadge');
    if (!badge) return;

    const countUrl = '/Reports/AppointmentReminders?handler=Count';
    let toastShown = false;

    function updateBadge(count) {
        if (count > 0) {
            badge.textContent = count > 99 ? '99+' : String(count);
            badge.classList.remove('d-none');
            if (!toastShown) {
                toastShown = true;
                showToast(count);
            }
        } else {
            badge.classList.add('d-none');
            toastShown = false;
        }
    }

    function showToast(count) {
        const text = count === 1
            ? '1 appointment is within 15 minutes.'
            : count + ' appointments are within 15 minutes.';
        const el = document.createElement('div');
        el.className = 'appt-reminder-toast alert alert-warning alert-dismissible fade show';
        el.setAttribute('role', 'alert');
        el.innerHTML = '<strong>Appointment Reminder</strong> — ' + text +
            ' <a href="/Reports/AppointmentReminders" class="alert-link ms-1">View</a>' +
            '<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>';
        document.body.appendChild(el);
        setTimeout(function () {
            if (el.parentNode) el.remove();
        }, 30000);
    }

    function poll() {
        fetch(countUrl, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : { count: 0 }; })
            .then(function (data) { updateBadge(data.count || 0); })
            .catch(function () { });
    }

    poll();
    setInterval(poll, 60000);
})();

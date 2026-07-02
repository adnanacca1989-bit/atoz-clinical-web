(function () {
    const bell = document.getElementById('clinicalNotifyBell');
    const badge = document.getElementById('clinicalNotifyBadge');
    const menu = document.getElementById('clinicalNotifyMenu');
    const list = document.getElementById('clinicalNotifyList');
    const clearBtn = document.getElementById('clinicalNotifyClearBtn');
    if (!bell || !menu || !list) return;

    const STORAGE_KEY = 'clinicalNotifyItems';
    const CLEARED_KEY = 'clinicalNotifyClearedAt';
    const LAST_POLL_KEY = 'clinicalNotifyLastPoll';
    const POLL_MS = 30000;
    const HUB_URL = '/hubs/notifications';

    const loadStored = () => {
        try {
            return JSON.parse(localStorage.getItem(STORAGE_KEY) || '[]');
        } catch {
            return [];
        }
    };

    const saveStored = (items) => {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(items));
    };

    const getClearedAt = () => {
        const raw = localStorage.getItem(CLEARED_KEY);
        return raw ? Number(raw) : 0;
    };

    const getLastPoll = () => {
        const raw = localStorage.getItem(LAST_POLL_KEY);
        return raw ? Number(raw) : 0;
    };

    const setLastPoll = (ticks) => {
        localStorage.setItem(LAST_POLL_KEY, String(ticks));
    };

    const escapeHtml = (text) => {
        const div = document.createElement('div');
        div.textContent = text ?? '';
        return div.innerHTML;
    };

    const playNotifySound = () => {
        try {
            const ctx = new (window.AudioContext || window.webkitAudioContext)();
            const playTone = (freq, start, duration) => {
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.value = freq;
                gain.gain.setValueAtTime(0.0001, start);
                gain.gain.exponentialRampToValueAtTime(0.18, start + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, start + duration);
                osc.connect(gain);
                gain.connect(ctx.destination);
                osc.start(start);
                osc.stop(start + duration);
            };
            const now = ctx.currentTime;
            playTone(880, now, 0.12);
            playTone(1174.66, now + 0.1, 0.14);
        } catch { /* ignore */ }
    };

    const mergeItems = (incoming) => {
        const clearedAt = getClearedAt();
        const map = new Map();
        for (const item of loadStored()) {
            if ((item.atUtc || 0) > clearedAt) map.set(item.id, item);
        }
        for (const item of incoming || []) {
            if ((item.atUtc || 0) > clearedAt) map.set(item.id, item);
        }
        const merged = [...map.values()].sort((a, b) => (b.atUtc || 0) - (a.atUtc || 0));
        saveStored(merged);
        return merged;
    };

    const render = (items) => {
        list.innerHTML = '';
        if (!items.length) {
            list.innerHTML = '<li class="dropdown-item text-muted small">No notifications.</li>';
        } else {
            items.forEach(item => {
                const li = document.createElement('li');
                li.className = 'dropdown-item-text clinical-notify-item';
                li.style.cursor = item.link ? 'pointer' : 'default';
                li.innerHTML = `<div class="fw-semibold">${escapeHtml(item.title || 'Notification')}</div>
                    <div class="small text-muted">${escapeHtml(item.detail || '')}</div>
                    <div class="small text-muted">${escapeHtml(item.at || '')}</div>`;
                if (item.link) {
                    li.addEventListener('click', () => { window.location.href = item.link; });
                }
                list.appendChild(li);
            });
        }

        if (badge) {
            if (items.length > 0) {
                badge.textContent = items.length > 99 ? '99+' : String(items.length);
                badge.classList.remove('d-none');
            } else {
                badge.classList.add('d-none');
            }
        }
    };

    const applyIncoming = (incoming, playSound) => {
        const lastPoll = getLastPoll();
        const clearedAt = getClearedAt();
        let shouldPlay = false;
        for (const item of incoming || []) {
            if ((item.atUtc || 0) > lastPoll && (item.atUtc || 0) > clearedAt) {
                shouldPlay = true;
                break;
            }
        }
        const merged = mergeItems(incoming || []);
        if (playSound && shouldPlay && lastPoll > 0) playNotifySound();
        render(merged);
        return merged;
    };

    const poll = async () => {
        try {
            const stored = loadStored();
            const oldest = stored.length ? Math.min(...stored.map(i => i.atUtc || Date.now())) : Date.now() - 30 * 86400000;
            const sinceTicks = oldest - 60000;
            const res = await fetch(`/Notifications/Feed?sinceTicks=${sinceTicks}`, {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            });
            if (!res.ok) return;
            const data = await res.json();
            applyIncoming(data.items || [], true);
            if (data.serverTime) setLastPoll(data.serverTime);
        } catch { /* ignore */ }
    };

    const clearNotifications = () => {
        localStorage.setItem(CLEARED_KEY, String(Date.now()));
        saveStored([]);
        setLastPoll(Date.now());
        render([]);
    };

    const connectRealtime = () => {
        if (!window.signalR) return;
        try {
            const connection = new signalR.HubConnectionBuilder()
                .withUrl(HUB_URL)
                .withAutomaticReconnect()
                .build();

            connection.on('ReceiveNotification', (item) => {
                if (!item || !item.id) return;
                const clearedAt = getClearedAt();
                if ((item.atUtc || 0) <= clearedAt) return;
                applyIncoming([item], true);
                setLastPoll(Math.max(getLastPoll(), item.atUtc || Date.now()));
            });

            connection.start().catch(() => { /* fallback to polling */ });
        } catch { /* ignore */ }
    };

    clearBtn?.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        clearNotifications();
    });

    render(loadStored().filter(i => (i.atUtc || 0) > getClearedAt()));
    poll();
    setInterval(poll, POLL_MS);
    connectRealtime();
})();

(function () {
    'use strict';

    let navConnection = null;
    let pageConnection = null;

    const escapeHtml = (text) => {
        const div = document.createElement('div');
        div.textContent = text ?? '';
        return div.innerHTML;
    };

    const formatTime = (iso) => {
        if (!iso) return '';
        const d = new Date(iso);
        if (Number.isNaN(d.getTime())) return '';
        const now = new Date();
        const sameDay = d.toDateString() === now.toDateString();
        return sameDay
            ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
            : d.toLocaleDateString([], { month: 'short', day: 'numeric' }) + ' ' +
              d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    };

    const initials = (name) => {
        if (!name) return '?';
        const parts = name.trim().split(/\s+/);
        if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
        return name.slice(0, 2).toUpperCase();
    };

    const hasSignalR = () => typeof signalR !== 'undefined';

    const isConnected = (connection) =>
        hasSignalR() &&
        connection &&
        connection.state === signalR.HubConnectionState.Connected;

    async function buildConnection(hubUrl) {
        if (!hasSignalR()) return null;
        return new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000, 60000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();
    }

    async function startConnection(connection, label) {
        if (!connection) return false;
        try {
            if (connection.state === signalR.HubConnectionState.Connected) return true;
            await connection.start();
            return true;
        } catch (err) {
            console.warn(`Chat ${label} connect failed, retrying...`, err);
            setTimeout(() => startConnection(connection, label), 5000);
            return false;
        }
    }

    function updateNavBadge(count) {
        const badge = document.getElementById('clinicalChatBadge');
        if (!badge) return;
        if (count > 0) {
            badge.textContent = count > 99 ? '99+' : String(count);
            badge.classList.remove('d-none');
        } else {
            badge.classList.add('d-none');
        }
    }

    async function refreshNavUnread(unreadUrl) {
        if (!unreadUrl) return;
        try {
            const res = await fetch(unreadUrl, { credentials: 'same-origin' });
            if (!res.ok) return;
            const data = await res.json();
            updateNavBadge(data.count ?? 0);
        } catch {
            /* ignore */
        }
    }

    async function sendViaHttp(options, recipientUserId, text, attachmentId) {
        const formData = new FormData();
        formData.append('recipientUserId', recipientUserId);
        if (text) formData.append('body', text);
        if (attachmentId) formData.append('attachmentId', attachmentId);
        if (options.antiforgeryToken) {
            formData.append('__RequestVerificationToken', options.antiforgeryToken);
        }

        const res = await fetch(options.sendUrl, {
            method: 'POST',
            body: formData,
            credentials: 'same-origin'
        });

        const data = await res.json().catch(() => ({}));
        if (!res.ok) {
            throw new Error(data.error ?? 'Could not send message.');
        }
        return data;
    }

    window.initClinicalChatNav = function (options) {
        const badge = document.getElementById('clinicalChatBadge');
        if (!badge || !options?.hubUrl) return;

        refreshNavUnread(options.unreadUrl);

        if (!hasSignalR()) {
            setInterval(() => refreshNavUnread(options.unreadUrl), 60000);
            return;
        }

        buildConnection(options.hubUrl).then((conn) => {
            if (!conn) return;
            navConnection = conn;

            conn.on('ReceiveMessage', (msg) => {
                if (!msg?.isMine) {
                    refreshNavUnread(options.unreadUrl);
                    try {
                        const ctx = new (window.AudioContext || window.webkitAudioContext)();
                        const osc = ctx.createOscillator();
                        const gain = ctx.createGain();
                        osc.frequency.value = 880;
                        gain.gain.value = 0.08;
                        osc.connect(gain);
                        gain.connect(ctx.destination);
                        osc.start();
                        osc.stop(ctx.currentTime + 0.08);
                    } catch { /* ignore */ }
                }
            });

            conn.onreconnected(() => refreshNavUnread(options.unreadUrl));

            startConnection(conn, 'nav');
        });

        setInterval(() => refreshNavUnread(options.unreadUrl), 60000);
    };

    window.initClinicalChatPage = function (options) {
        const userList = document.getElementById('chatUserList');
        const placeholder = document.getElementById('chatPlaceholder');
        const active = document.getElementById('chatActive');
        const messagesEl = document.getElementById('chatMessages');
        const input = document.getElementById('chatMessageInput');
        const sendBtn = document.getElementById('chatSendBtn');
        const attachBtn = document.getElementById('chatAttachBtn');
        const fileInput = document.getElementById('chatFileInput');
        const searchInput = document.getElementById('chatUserSearch');
        const refreshBtn = document.getElementById('chatRefreshUsersBtn');
        const statusEl = document.getElementById('chatConnectionStatus');

        if (!userList || !options?.currentUserId || !options?.sendUrl) return;

        let users = [];
        let onlineSet = new Set();
        let selectedPeer = null;
        let pendingAttachment = null;

        const peerNameEl = document.getElementById('chatPeerName');
        const peerStatusEl = document.getElementById('chatPeerStatus');
        const peerDotEl = document.getElementById('chatPeerDot');

        function setConnectionStatus(text, isError) {
            if (!statusEl) return;
            statusEl.textContent = text;
            statusEl.classList.toggle('text-danger', !!isError);
            statusEl.classList.toggle('text-muted', !isError);
        }

        function renderUsers(filter) {
            const term = (filter ?? '').trim().toLowerCase();
            const filtered = users.filter(u =>
                !term || u.name.toLowerCase().includes(term) || (u.role ?? '').toLowerCase().includes(term));

            if (filtered.length === 0) {
                userList.innerHTML = '<div class="clinical-chat-empty">No staff found.</div>';
                return;
            }

            userList.innerHTML = filtered.map(u => `
                <div class="clinical-chat-user-item${selectedPeer?.userId === u.userId ? ' active' : ''}"
                     data-user-id="${escapeHtml(u.userId)}">
                    <div class="clinical-chat-avatar">
                        ${escapeHtml(initials(u.name))}
                        <span class="clinical-chat-avatar-dot${u.isOnline ? ' online' : ''}"></span>
                    </div>
                    <div class="clinical-chat-user-meta">
                        <div class="clinical-chat-user-name">${escapeHtml(u.name)}</div>
                        <div class="clinical-chat-user-preview">${escapeHtml(u.lastPreview ?? u.role ?? '')}</div>
                    </div>
                    <div class="clinical-chat-user-side">
                        <div class="clinical-chat-user-time">${formatTime(u.lastMessageAt)}</div>
                        ${u.unreadCount > 0 ? `<span class="clinical-chat-unread-badge">${u.unreadCount}</span>` : ''}
                    </div>
                </div>
            `).join('');

            userList.querySelectorAll('.clinical-chat-user-item').forEach(el => {
                el.addEventListener('click', () => {
                    const id = el.getAttribute('data-user-id');
                    const user = users.find(x => x.userId === id);
                    if (user) selectPeer(user);
                });
            });
        }

        async function loadUsers() {
            try {
                const res = await fetch(options.usersUrl, { credentials: 'same-origin' });
                if (!res.ok) throw new Error('Failed');
                users = await res.json();
                users.forEach(u => { u.isOnline = onlineSet.has(u.userId); });
                renderUsers(searchInput?.value ?? '');
                if (selectedPeer) {
                    const refreshed = users.find(u => u.userId === selectedPeer.userId);
                    if (refreshed) selectedPeer = refreshed;
                    updatePeerHeader();
                }
            } catch {
                userList.innerHTML = '<div class="clinical-chat-empty">Could not load users.</div>';
            }
        }

        function updatePeerHeader() {
            if (!selectedPeer) return;
            peerNameEl.textContent = selectedPeer.name;
            const online = onlineSet.has(selectedPeer.userId);
            peerStatusEl.textContent = online ? 'Online' : 'Offline';
            peerDotEl.classList.toggle('online', online);
        }

        function buildDownloadUrl(attachmentId) {
            const url = new URL(options.downloadUrl, window.location.origin);
            url.searchParams.set('id', attachmentId);
            return url.pathname + url.search;
        }

        function renderMessage(msg) {
            const mine = msg.isMine || msg.senderUserId === options.currentUserId;
            const bubble = document.createElement('div');
            bubble.className = `clinical-chat-bubble ${mine ? 'mine' : 'theirs'}`;
            bubble.dataset.messageId = msg.id;

            let html = '';
            if (msg.attachmentId) {
                const url = buildDownloadUrl(msg.attachmentId);
                const label = msg.attachmentFileName ?? 'Download file';
                html += `<a class="clinical-chat-attachment" href="${url}" target="_blank" rel="noopener">📎 ${escapeHtml(label)}</a>`;
            }
            if (msg.body) html += `<div>${escapeHtml(msg.body)}</div>`;
            html += `<span class="clinical-chat-bubble-time">${formatTime(msg.sentAt)}</span>`;
            bubble.innerHTML = html;
            return bubble;
        }

        async function loadHistory(peerUserId) {
            messagesEl.innerHTML = '<div class="clinical-chat-empty">Loading...</div>';
            try {
                const res = await fetch(`${options.historyUrl}&peerUserId=${encodeURIComponent(peerUserId)}`, {
                    credentials: 'same-origin'
                });
                if (!res.ok) throw new Error('Failed');
                const messages = await res.json();
                messagesEl.innerHTML = '';
                if (messages.length === 0) {
                    messagesEl.innerHTML = '<div class="clinical-chat-empty">No messages yet. Say hello!</div>';
                } else {
                    messages.forEach(m => messagesEl.appendChild(renderMessage(m)));
                    messagesEl.scrollTop = messagesEl.scrollHeight;
                }
                if (isConnected(pageConnection)) {
                    await pageConnection.invoke('MarkRead', peerUserId);
                } else {
                    const fd = new FormData();
                    fd.append('peerUserId', peerUserId);
                    if (options.antiforgeryToken) {
                        fd.append('__RequestVerificationToken', options.antiforgeryToken);
                    }
                    await fetch(options.markReadUrl, { method: 'POST', body: fd, credentials: 'same-origin' });
                }
                await loadUsers();
            } catch {
                messagesEl.innerHTML = '<div class="clinical-chat-empty">Could not load messages.</div>';
            }
        }

        function selectPeer(user) {
            selectedPeer = user;
            placeholder.classList.add('d-none');
            active.classList.remove('d-none');
            updatePeerHeader();
            renderUsers(searchInput?.value ?? '');
            loadHistory(user.userId);
            input?.focus();
        }

        function appendMessage(msg) {
            if (!selectedPeer) return;
            const peerId = selectedPeer.userId;
            if (msg.senderUserId !== peerId && msg.recipientUserId !== peerId &&
                msg.senderUserId !== options.currentUserId) return;

            const empty = messagesEl.querySelector('.clinical-chat-empty');
            if (empty) empty.remove();

            if (messagesEl.querySelector(`[data-message-id="${msg.id}"]`)) return;

            const normalized = {
                ...msg,
                isMine: msg.senderUserId === options.currentUserId
            };
            messagesEl.appendChild(renderMessage(normalized));
            messagesEl.scrollTop = messagesEl.scrollHeight;
        }

        async function deliverMessage(recipientUserId, text, attachmentId) {
            if (isConnected(pageConnection)) {
                try {
                    await pageConnection.invoke(
                        'SendMessage',
                        recipientUserId,
                        text || null,
                        attachmentId ?? null);
                    return;
                } catch (err) {
                    console.warn('SignalR send failed, using HTTP fallback.', err);
                }
            }

            const msg = await sendViaHttp(options, recipientUserId, text, attachmentId);
            appendMessage(msg);
        }

        async function sendMessage() {
            if (!selectedPeer) {
                alert('Select a colleague to message.');
                return;
            }

            const text = (input?.value ?? '').trim();
            if (!text && !pendingAttachment) return;

            sendBtn.disabled = true;
            try {
                await deliverMessage(
                    selectedPeer.userId,
                    text || null,
                    pendingAttachment?.attachmentId ?? null);
                input.value = '';
                pendingAttachment = null;
                await loadUsers();
            } catch (err) {
                console.error(err);
                alert(err.message ?? 'Could not send message. Please try again.');
            } finally {
                sendBtn.disabled = false;
            }
        }

        attachBtn?.addEventListener('click', () => fileInput?.click());

        fileInput?.addEventListener('change', async () => {
            const file = fileInput.files?.[0];
            fileInput.value = '';
            if (!file || !selectedPeer) return;

            if (file.size > 5 * 1024 * 1024) {
                alert('File exceeds 5 MB limit.');
                return;
            }

            const formData = new FormData();
            formData.append('file', file);
            if (options.antiforgeryToken) {
                formData.append('__RequestVerificationToken', options.antiforgeryToken);
            }

            sendBtn.disabled = true;
            try {
                const res = await fetch(options.uploadUrl, {
                    method: 'POST',
                    body: formData,
                    credentials: 'same-origin'
                });
                const data = await res.json();
                if (!res.ok) {
                    alert(data.error ?? 'Upload failed.');
                    return;
                }
                await deliverMessage(
                    selectedPeer.userId,
                    input?.value?.trim() || null,
                    data.attachmentId);
                if (input) input.value = '';
                pendingAttachment = null;
                await loadUsers();
            } catch (err) {
                alert(err.message ?? 'Upload failed.');
            } finally {
                sendBtn.disabled = false;
            }
        });

        sendBtn?.addEventListener('click', sendMessage);
        input?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });

        searchInput?.addEventListener('input', () => renderUsers(searchInput.value));
        refreshBtn?.addEventListener('click', loadUsers);

        if (!hasSignalR()) {
            setConnectionStatus('Real-time updates unavailable — messages still send via server.', false);
        } else {
            buildConnection(options.hubUrl).then((conn) => {
                if (!conn) {
                    setConnectionStatus('Real-time connection unavailable — messages still send via server.', false);
                    return;
                }

                pageConnection = conn;

                conn.on('ReceiveMessage', (msg) => {
                    appendMessage(msg);
                    loadUsers();
                });

                conn.on('PresenceChanged', (onlineIds) => {
                    onlineSet = new Set(onlineIds ?? []);
                    users.forEach(u => { u.isOnline = onlineSet.has(u.userId); });
                    renderUsers(searchInput?.value ?? '');
                    updatePeerHeader();
                });

                conn.on('MessagesRead', () => loadUsers());

                conn.onreconnected(() => {
                    setConnectionStatus('Connected', false);
                    if (selectedPeer) loadHistory(selectedPeer.userId);
                });

                conn.onclose(() => setConnectionStatus('Reconnecting…', true));

                startConnection(conn, 'page').then((ok) => {
                    setConnectionStatus(ok ? 'Connected' : 'Connecting…', !ok);
                });
            });
        }

        loadUsers();
    };
})();

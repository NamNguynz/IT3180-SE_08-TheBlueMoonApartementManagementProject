(() => {
    const ROLE_LABEL = { BQT: 'Ban Quản Trị', KE_TOAN: 'Kế Toán', CU_DAN: 'Cư Dân' };

    const el = (id) => document.getElementById(id);

    const setBusy = (btn, busy) => {
        if (!btn || !btn.isConnected) return;
        btn.disabled = busy;
        btn.classList.toggle('is-spinning', busy);
    };

    const showAlert = (message, ok) => {
        const box = el('approvalAlert');
        box.className = 'alert' + (ok ? ' alert-success' : '');
        box.textContent = message;
        box.hidden = false;
    };

    const api = async (url, options = {}) => {
        const res = await fetch(url, {
            credentials: 'same-origin',
            ...options,
            headers: { 'Accept': 'application/json', ...(options.headers || {}) }
        });

        if (res.status === 401) {
            location.assign('/login');
            return { ok: false, status: 401, data: null };
        }

        let data = null;
        try { data = await res.json(); } catch { /* 204 hoặc không phải JSON */ }
        return { ok: res.ok, status: res.status, data };
    };

    const renderRow = (user) => {
        const tr = document.createElement('tr');

        const nameTd = document.createElement('td');
        nameTd.textContent = user.fullName?.trim() || 'Unnamed';
        tr.appendChild(nameTd);

        const userTd = document.createElement('td');
        userTd.textContent = user.username;
        tr.appendChild(userTd);

        const roleTd = document.createElement('td');
        const badge = document.createElement('span');
        badge.className = 'badge';
        badge.textContent = ROLE_LABEL[user.role] ?? user.role;
        roleTd.appendChild(badge);
        tr.appendChild(roleTd);

        const dateTd = document.createElement('td');
        dateTd.textContent = new Date(user.createdAt).toLocaleString('vi-VN', {
            day: '2-digit', month: '2-digit', year: 'numeric',
            hour: '2-digit', minute: '2-digit'
        });
        tr.appendChild(dateTd);

        const actionTd = document.createElement('td');
        actionTd.className = 'actions';
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn-approve';
        btn.textContent = 'Xác nhận';
        btn.dataset.approveId = user.id;
        actionTd.appendChild(btn);
        tr.appendChild(actionTd);

        return tr;
    };

    const render = (list) => {
        el('approvalLoading').hidden = true;
        el('approvalBody').replaceChildren(...list.map(renderRow));
        el('approvalEmpty').hidden = list.length > 0;
        el('approvalTable').hidden = list.length === 0;
    };

    const loadPending = async () => {
        try {
            const result = await api('/api/tai-khoan/cho-xac-nhan');
            if (result.status === 401) return;

            if (!result.ok) {
                el('approvalLoading').hidden = true;
                showAlert(result.data?.message || 'Không tải được danh sách. Vui lòng thử lại.', false);
                return;
            }

            render(result.data ?? []);
        } catch {
            el('approvalLoading').hidden = true;
            showAlert('Lỗi kết nối. Vui lòng kiểm tra mạng và thử lại.', false);
        }
    };

    const refresh = async (btn) => {
        setBusy(btn, true);
        try {
            await loadPending();
        } finally {
            setBusy(btn, false);
        }
    };

    const approve = async (btn) => {
        setBusy(btn, true);
        try {
            const result = await api(`/api/tai-khoan/${btn.dataset.approveId}/xac-nhan`, { method: 'POST' });
            if (result.status === 401) return;

            showAlert(
                result.ok
                    ? 'Đã xác nhận tài khoản.'
                    : (result.data?.message || 'Không thể xác nhận tài khoản. Vui lòng thử lại.'),
                result.ok
            );

            // Luôn tải lại: danh sách đúng và nút được dựng lại từ đầu
            await loadPending();
        } catch {
            showAlert('Lỗi kết nối. Vui lòng kiểm tra mạng và thử lại.', false);
        } finally {
            setBusy(btn, false);
        }
    };

    // Gán trực tiếp thay vì addEventListener: gọi lại nhiều lần vẫn chỉ có một handler
    document.onclick = (e) => {
        const refreshBtn = e.target.closest?.('#approvalRefresh');
        if (refreshBtn) return refresh(refreshBtn);

        const approveBtn = e.target.closest?.('[data-approve-id]');
        if (approveBtn) return approve(approveBtn);
    };

    const init = () => {
        if (!el('approvalBody')) return;
        loadPending();
    };

    init();

    if (window.Blazor?.addEventListener) {
        Blazor.addEventListener('enhancedload', init);
    }
})();
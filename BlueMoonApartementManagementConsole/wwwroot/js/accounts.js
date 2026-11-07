(() => {
    const ROLE_LABEL = { BQT: 'Ban Quản Trị', KE_TOAN: 'Kế Toán', CU_DAN: 'Cư Dân' };
    const ENDPOINT = { disable: 'vo-hieu-hoa', enable: 'kich-hoat', unlock: 'mo-khoa', reset: 'cap-lai-mat-khau' };

    const el = (id) => document.getElementById(id);
    let accounts = [];

    const showAlert = (message, ok) => {
        const box = el('accAlert');
        box.className = 'alert' + (ok ? ' alert-success' : '');
        box.textContent = message;
        box.hidden = false;
    };

    const api = async (url, options = {}) => {
        const headers = { 'Accept': 'application/json' };
        if (options.body) headers['Content-Type'] = 'application/json';

        const res = await fetch(url, {
            method: options.method || 'GET',
            credentials: 'same-origin',
            headers,
            body: options.body ? JSON.stringify(options.body) : undefined
        });

        if (res.status === 401) {
            location.assign('/login');
            return { ok: false, status: 401, data: null };
        }

        let data = null;
        try { data = await res.json(); } catch { /* 204 hoặc không phải JSON */ }
        return { ok: res.ok, status: res.status, data };
    };

    const formatTime = (iso) =>
        new Date(iso).toLocaleString('vi-VN', {
            day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
        });

    const isLocked = (a) => a.lockedUntil && new Date(a.lockedUntil) > new Date();

    const showTemp = (username, password) => {
        el('tempUser').textContent = username;
        el('tempValue').textContent = password;
        el('tempBox').hidden = false;
    };

    const makeBtn = (label, action, id, extraClass = '') => {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = `btn-link ${extraClass}`.trim();
        b.textContent = label;
        b.dataset.action = action;
        b.dataset.id = id;
        return b;
    };

    const renderRow = (a, ownerName) => {
        const tr = document.createElement('tr');

        const text = (value, cls) => {
            const td = document.createElement('td');
            if (cls) td.className = cls;
            td.textContent = value;
            return td;
        };

        tr.appendChild(text(a.username));
        tr.appendChild(text(a.fullName?.trim() || 'Unnamed'));

        const roleTd = document.createElement('td');
        const roleBadge = document.createElement('span');
        roleBadge.className = 'badge';
        roleBadge.textContent = ROLE_LABEL[a.role] ?? a.role;
        roleTd.appendChild(roleBadge);
        tr.appendChild(roleTd);

        tr.appendChild(text(ownerName ?? '—'));

        const statusTd = document.createElement('td');
        const statusBadge = document.createElement('span');
        if (!a.isActive) {
            statusBadge.className = 'badge badge-muted';
            statusBadge.textContent = 'Vô hiệu hóa';
        } else if (isLocked(a)) {
            statusBadge.className = 'badge badge-warn';
            statusBadge.textContent = `Khóa đến ${formatTime(a.lockedUntil)}`;
        } else {
            statusBadge.className = 'badge badge-ok';
            statusBadge.textContent = 'Đang hoạt động';
        }
        statusTd.appendChild(statusBadge);
        tr.appendChild(statusTd);

        const actionTd = document.createElement('td');
        if (a.canManage) {
            const wrap = document.createElement('div');
            wrap.className = 'row-actions';
            if (a.isActive) wrap.appendChild(makeBtn('Vô hiệu hóa', 'disable', a.id, 'danger'));
            else wrap.appendChild(makeBtn('Kích hoạt', 'enable', a.id));
            if (isLocked(a)) wrap.appendChild(makeBtn('Mở khóa', 'unlock', a.id));
            wrap.appendChild(makeBtn('Cấp lại mật khẩu', 'reset', a.id));
            actionTd.appendChild(wrap);
        } else {
            actionTd.textContent = '—';
        }
        tr.appendChild(actionTd);

        return tr;
    };

    const render = () => {
        const form = el('accFilter');
        const q = form.elements.namedItem('q').value.trim().toLowerCase();
        const role = form.elements.namedItem('role').value;
        const status = form.elements.namedItem('status').value;

        const names = new Map(accounts.map((a) => [a.id, a.username]));

        const list = accounts.filter((a) =>
            (!q || a.username.toLowerCase().includes(q) || (a.fullName ?? '').toLowerCase().includes(q)) &&
            (!role || a.role === role) &&
            (!status || (status === 'active' ? a.isActive : !a.isActive))
        );

        el('accLoading').hidden = true;
        el('accBody').replaceChildren(...list.map((a) => renderRow(a, names.get(a.grantedById))));
        el('accEmpty').hidden = list.length > 0;
        el('accTable').hidden = list.length === 0;
    };

    const load = async () => {
        const r = await api('/api/tai-khoan');
        if (r.status === 401) return;

        if (!r.ok) {
            el('accLoading').hidden = true;
            showAlert(r.data?.message || 'Không tải được danh sách.', false);
            return;
        }

        accounts = r.data ?? [];
        render();
    };

    const onAction = async (btn) => {
        const { action, id } = btn.dataset;
        const acc = accounts.find((a) => String(a.id) === id);

        if (action === 'disable' && !window.confirm(`Vô hiệu hóa tài khoản ${acc?.username}?`)) return;
        if (action === 'reset' && !window.confirm(`Cấp lại mật khẩu cho ${acc?.username}? Mật khẩu cũ sẽ không dùng được nữa.`)) return;

        btn.disabled = true;
        try {
            const r = await api(`/api/tai-khoan/${id}/${ENDPOINT[action]}`, { method: 'POST' });
            if (r.status === 401) return;

            if (!r.ok) {
                showAlert(r.data?.message || 'Thao tác thất bại.', false);
                return;
            }

            if (r.data?.tempPassword) showTemp(acc.username, r.data.tempPassword);
            showAlert('Đã thực hiện thao tác.', true);
            await load();
        } catch {
            showAlert('Lỗi kết nối. Vui lòng thử lại.', false);
        } finally {
            btn.disabled = false;
        }
    };

    const onCreate = async (form) => {
        const body = {
            username: form.elements.namedItem('username').value.trim(),
            fullName: form.elements.namedItem('fullName').value.trim(),
            role: form.elements.namedItem('role').value
        };

        try {
            const r = await api('/api/tai-khoan', { method: 'POST', body });
            if (r.status === 401) return;

            if (!r.ok) {
                showAlert(r.data?.message || 'Không tạo được tài khoản.', false);
                return;
            }

            form.reset();
            showTemp(body.username, r.data.tempPassword);
            showAlert('Đã tạo tài khoản.', true);
            await load();
        } catch {
            showAlert('Lỗi kết nối. Vui lòng thử lại.', false);
        }
    };

    const onClick = (e) => {
        const actionBtn = e.target.closest?.('[data-action]');
        if (actionBtn) return onAction(actionBtn);

        if (e.target.closest?.('#tempCopy')) {
            navigator.clipboard?.writeText(el('tempValue').textContent);
            return;
        }

        if (e.target.closest?.('#tempClose')) el('tempBox').hidden = true;
    };

    const onSubmit = (e) => {
        if (e.target.id === 'createForm') {
            e.preventDefault();
            return onCreate(e.target);
        }
        if (e.target.id === 'accFilter') e.preventDefault();
    };

    const onFilterChange = (e) => {
        if (e.target.closest?.('#accFilter')) render();
    };

    const init = () => {
        if (!el('accBody')) return;

        if (!window.__accBound) {
            document.addEventListener('click', onClick);
            document.addEventListener('submit', onSubmit);
            document.addEventListener('input', onFilterChange);
            document.addEventListener('change', onFilterChange);
            window.__accBound = true;
        }

        load();
    };

    init();

    if (window.Blazor?.addEventListener) {
        Blazor.addEventListener('enhancedload', init);
    }
})();
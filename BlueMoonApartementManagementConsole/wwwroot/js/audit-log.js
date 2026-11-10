(() => {
    const PAGE_SIZE = 20;
    const VN_OFFSET = '+07:00';

    const el = (id) => document.getElementById(id);

    // Nhãn tiếng Việt lấy từ các option của ô lọc Hành động (trang sinh ra từ AuditActionLabels.cs),
    // để chỉ phải khai báo nhãn ở một chỗ. Dùng elements.namedItem: select name="action" che mất form.action.
    const actionLabels = () => Object.fromEntries(
        [...el('auditFilter').elements.namedItem('action').options]
            .filter((o) => o.value)
            .map((o) => [o.value, o.textContent.trim()]));

    const showAlert = (message) => {
        const box = el('auditAlert');
        box.className = 'alert';
        box.textContent = message;
        box.hidden = false;
    };

    const api = async (url) => {
        const res = await fetch(url, {
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        });

        if (res.status === 401) {
            location.assign('/login');
            return { ok: false, status: 401, data: null };
        }

        let data = null;
        try { data = await res.json(); } catch { /* không phải JSON */ }
        return { ok: res.ok, status: res.status, data };
    };

    // Đọc bộ lọc từ form và đổi sang khoảng thời gian UTC đúng với múi giờ Việt Nam
    const buildQuery = (page) => {
        const form = el('auditFilter');
        const field = (name) => form.elements.namedItem(name)?.value?.trim() ?? '';

        const params = new URLSearchParams();
        const from = field('from');
        const to = field('to');
        const action = field('action');
        const username = field('username');

        if (from) {
            params.set('from', new Date(Date.parse(`${from}T00:00:00${VN_OFFSET}`)).toISOString());
        }
        if (to) {
            // Bao gồm cả ngày "đến ngày": lấy đầu ngày kế tiếp làm mốc loại trừ
            const nextDay = Date.parse(`${to}T00:00:00${VN_OFFSET}`) + 24 * 60 * 60 * 1000;
            params.set('to', new Date(nextDay).toISOString());
        }
        if (action) params.set('action', action);
        if (username) params.set('username', username);

        params.set('page', page);
        params.set('pageSize', PAGE_SIZE);
        return params.toString();
    };

    const formatTime = (iso) => {
        const d = new Date(iso);
        const text = d.toLocaleString('vi-VN', {
            timeZone: 'Asia/Ho_Chi_Minh',
            day: '2-digit', month: '2-digit', year: 'numeric',
            hour: '2-digit', minute: '2-digit', second: '2-digit',
            hour12: false
        });
        return `${text}.${String(d.getMilliseconds()).padStart(3, '0')}`;
    };

    const renderRow = (labels) => (log) => {
        const tr = document.createElement('tr');

        const cells = [
            { text: formatTime(log.occurredAt), mono: true },
            { text: log.username || 'Unnamed' },
            { text: labels[log.action] ?? log.action, badge: true },
            { text: log.target ?? '—', mono: true },
            { text: log.detail ?? '—', wrap: true }
        ];

        cells.forEach((c) => {
            const td = document.createElement('td');
            if (c.mono) td.className = 'mono';
            if (c.wrap) td.className = 'col-wrap';

            if (c.badge) {
                const span = document.createElement('span');
                span.className = 'badge';
                span.textContent = c.text;
                td.appendChild(span);
            } else {
                td.textContent = c.text;
            }
            tr.appendChild(td);
        });

        return tr;
    };

    const render = (result) => {
        el('auditLoading').hidden = true;

        const total = result.total;
        const page = result.page;
        const totalPages = Math.max(1, Math.ceil(total / result.pageSize));

        el('auditBody').replaceChildren(...result.items.map(renderRow(actionLabels())));
        el('auditEmpty').hidden = total > 0;
        el('auditTable').hidden = total === 0;
        el('auditPager').hidden = total === 0;

        el('auditPageInfo').textContent = `Trang ${page} / ${totalPages} (${total} bản ghi)`;

        const prev = el('auditPrev');
        const next = el('auditNext');
        prev.disabled = page <= 1;
        prev.dataset.page = page - 1;
        next.disabled = page >= totalPages;
        next.dataset.page = page + 1;
    };

    const load = async (page = 1) => {
        try {
            const result = await api(`/api/nhat-ky?${buildQuery(page)}`);
            if (result.status === 401) return;

            if (!result.ok) {
                el('auditLoading').hidden = true;
                showAlert(result.data?.message || 'Không tải được nhật ký. Vui lòng thử lại.');
                return;
            }

            el('auditAlert').hidden = true;
            render(result.data);
        } catch {
            el('auditLoading').hidden = true;
            showAlert('Lỗi kết nối. Vui lòng kiểm tra mạng và thử lại.');
        }
    };

    const onClick = (e) => {
        const pageBtn = e.target.closest?.('#auditPrev, #auditNext');
        if (pageBtn && !pageBtn.disabled) {
            load(Number(pageBtn.dataset.page));
            return;
        }

        if (e.target.closest?.('#auditReset')) {
            el('auditFilter').reset();
            load(1);
        }
    };

    const onSubmit = (e) => {
        if (e.target.id !== 'auditFilter') return;
        e.preventDefault();
        load(1);
    };

    const init = () => {
        if (!el('auditBody')) return;

        // Gắn listener một lần duy nhất; không dùng document.onclick để không ghi đè trang khác
        if (!window.__auditBound) {
            document.addEventListener('click', onClick);
            document.addEventListener('submit', onSubmit);
            window.__auditBound = true;
        }

        load(1);
    };

    init();

    if (window.Blazor?.addEventListener) {
        Blazor.addEventListener('enhancedload', init);
    }
})();